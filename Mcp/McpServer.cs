using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace PowerRenameAvalonia.Mcp;

public sealed class McpServer
{
    private static readonly string[] SupportedProtocolVersions = { "2025-11-25", "2025-06-18", "2025-03-26", "2024-11-05" };
    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private const string Instructions =
        "WAV 오디오 에셋 및 BMS 차트 파일 일괄 리네이밍 & 동기화 도구입니다.\n" +
        "1. scan_wav_assets: 디렉토리 내의 WAV 파일들과 연동된 BMS(#WAV 정의) 상태를 진단/스캔합니다.\n" +
        "2. preview_rename: 실제 파일 변경 전 정규식/문자열 치환 및 대소문자 변환 결과를 시뮬레이션합니다.\n" +
        "3. apply_rename: 디스크 파일들의 이름을 일괄 변경하고 연동된 BMS 파일의 #WAV 정의도 함께 동기화합니다.\n" +
        "4. sync_bms_chart: BMS 파일과 실제 WAV 파일 목록을 비교하거나 #WAV 경로를 갱신합니다.";

    private readonly Stream _input;
    private readonly Stream _output;
    private readonly TextWriter _log;
    private readonly IReadOnlyList<McpTool> _tools;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly SemaphoreSlim _toolLock = new(1, 1);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _inflight = new();

    public McpServer(Stream input, Stream output, TextWriter log)
    {
        _input = input;
        _output = output;
        _log = log;
        _tools = McpTools.Create();
    }

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        _log.WriteLine("[mcp] wav-bms-renamer 시작 (stdio)");
        using var reader = new StreamReader(_input, new UTF8Encoding(false));
        var pending = new List<Task>();

        while (!ct.IsCancellationRequested)
        {
            string? line = await reader.ReadLineAsync(ct);
            if (line == null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;

            // 도구 핸들러는 동기 디스크 I/O라서 읽기 루프에서 직접 돌리면 긴 작업 중 취소 알림조차 못 읽는다.
            // 스레드 풀로 넘겨 루프를 비우고, 예외가 미관찰 Task로 서버를 죽이지 않도록 감싼다.
            string captured = line;
            pending.RemoveAll(t => t.IsCompleted);
            pending.Add(Task.Run(async () =>
            {
                try { await HandleLineAsync(captured, ct); }
                catch (Exception ex) { _log.WriteLine($"[mcp] 처리되지 않은 오류: {ex.Message}"); }
            }, CancellationToken.None));
        }

        await Task.WhenAll(pending);
        _log.WriteLine("[mcp] wav-bms-renamer 종료");
        return 0;
    }

    private async Task HandleLineAsync(string line, CancellationToken ct)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(line);
        }
        catch (JsonException ex)
        {
            await SendAsync(ErrorResponse(null, -32700, $"Parse error: {ex.Message}"));
            return;
        }

        if (node is JsonArray batch)
        {
            foreach (var item in batch)
            {
                if (item is JsonObject obj) await HandleMessageAsync(obj, ct);
            }
        }
        else if (node is JsonObject message)
        {
            await HandleMessageAsync(message, ct);
        }
        else
        {
            await SendAsync(ErrorResponse(null, -32600, "Invalid Request"));
        }
    }

    private async Task HandleMessageAsync(JsonObject message, CancellationToken ct)
    {
        var id = message["id"];
        // method가 문자열이 아니어도 GetValue가 던지지 않도록 안전하게 읽는다.
        string? method = message["method"] is JsonValue mv && mv.TryGetValue<string>(out var m) ? m : null;

        if (string.IsNullOrEmpty(method))
        {
            if (id != null)
            {
                await SendAsync(ErrorResponse(id, -32600, "Missing method"));
            }
            return;
        }

        if (id == null)
        {
            HandleNotification(method, message["params"] as JsonObject);
            return;
        }

        var idKey = id.ToJsonString();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _inflight[idKey] = cts;

        try
        {
            var result = await DispatchAsync(method, message["params"] as JsonObject, cts.Token);
            await SendAsync(SuccessResponse(id, result));
        }
        catch (OperationCanceledException)
        {
            await SendAsync(ErrorResponse(id, -32000, "Request cancelled"));
        }
        catch (McpRpcException ex)
        {
            await SendAsync(ErrorResponse(id, ex.Code, ex.Message, ex.ErrorData));
        }
        catch (Exception ex)
        {
            await SendAsync(ErrorResponse(id, -32603, $"Internal error: {ex.Message}"));
        }
        finally
        {
            _inflight.TryRemove(idKey, out _);
            cts.Dispose();
        }
    }

    private void HandleNotification(string method, JsonObject? @params)
    {
        if (method == "notifications/cancelled")
        {
            var targetId = @params?["requestId"]?.ToJsonString();
            if (targetId != null && _inflight.TryRemove(targetId, out var cts))
            {
                try { cts.Cancel(); } catch { }
            }
        }
    }

    private async Task<JsonNode?> DispatchAsync(string method, JsonObject? @params, CancellationToken ct)
    {
        return method switch
        {
            "initialize" => HandleInitialize(@params),
            "ping" => new JsonObject(),
            "tools/list" => HandleToolsList(),
            "tools/call" => await HandleToolsCallAsync(@params, ct),
            _ => throw new McpRpcException(-32601, $"Method not found: {method}")
        };
    }

    private static JsonObject HandleInitialize(JsonObject? @params)
    {
        string clientVersion = @params?["protocolVersion"]?.GetValue<string>() ?? SupportedProtocolVersions[0];
        string protocolVersion = Array.IndexOf(SupportedProtocolVersions, clientVersion) >= 0 ? clientVersion : SupportedProtocolVersions[0];

        return new JsonObject
        {
            ["protocolVersion"] = protocolVersion,
            ["capabilities"] = new JsonObject
            {
                ["tools"] = new JsonObject { ["listChanged"] = false }
            },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = "wav-bms-renamer",
                ["version"] = "1.0.0"
            },
            ["instructions"] = Instructions
        };
    }

    private JsonObject HandleToolsList()
    {
        var array = new JsonArray();
        foreach (var tool in _tools)
        {
            array.Add(new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["inputSchema"] = JsonNode.Parse(tool.InputSchemaJson)
            });
        }
        return new JsonObject { ["tools"] = array };
    }

    private async Task<JsonObject> HandleToolsCallAsync(JsonObject? @params, CancellationToken ct)
    {
        string? name = @params?["name"]?.GetValue<string>();
        if (string.IsNullOrEmpty(name))
            throw new McpRpcException(-32602, "tools/call: name is required");

        var tool = _tools.FirstOrDefault(t => t.Name == name);
        if (tool == null)
            throw new McpRpcException(-32601, $"Tool not found: {name}");

        var arguments = @params?["arguments"] as JsonObject ?? new JsonObject();
        // 파일을 바꾸는 도구가 동시에 겹쳐 돌면 같은 폴더를 서로 밟을 수 있어 호출은 순서대로 실행한다.
        await _toolLock.WaitAsync(ct);
        (string text, bool isError) result;
        try { result = await tool.ExecuteAsync(arguments, ct); }
        finally { _toolLock.Release(); }
        var (text, isError) = result;

        var content = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "text",
                ["text"] = text
            }
        };

        var response = new JsonObject { ["content"] = content };
        if (isError)
        {
            response["isError"] = true;
        }
        return response;
    }

    private async Task SendAsync(JsonObject payload)
    {
        string json = payload.ToJsonString(JsonOptions);
        byte[] bytes = Encoding.UTF8.GetBytes(json + "\n");

        await _writeLock.WaitAsync();
        try
        {
            await _output.WriteAsync(bytes);
            await _output.FlushAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static JsonObject SuccessResponse(JsonNode? id, JsonNode? result)
    {
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["result"] = result ?? new JsonObject()
        };
    }

    private static JsonObject ErrorResponse(JsonNode? id, int code, string message, JsonNode? data = null)
    {
        var error = new JsonObject
        {
            ["code"] = code,
            ["message"] = message
        };
        if (data != null) error["data"] = data.DeepClone();

        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["error"] = error
        };
    }
}

public sealed class McpRpcException : Exception
{
    public int Code { get; }
    public JsonNode? ErrorData { get; }

    public McpRpcException(int code, string message, JsonNode? errorData = null) : base(message)
    {
        Code = code;
        ErrorData = errorData;
    }
}

public sealed class McpTool
{
    public string Name { get; }
    public string Description { get; }
    public string InputSchemaJson { get; }
    public Func<JsonObject, CancellationToken, Task<(string text, bool isError)>> Handler { get; }

    public McpTool(string name, string description, string inputSchemaJson, Func<JsonObject, CancellationToken, Task<(string text, bool isError)>> handler)
    {
        Name = name;
        Description = description;
        InputSchemaJson = inputSchemaJson;
        Handler = handler;
    }

    public Task<(string text, bool isError)> ExecuteAsync(JsonObject args, CancellationToken ct) => Handler(args, ct);
}
