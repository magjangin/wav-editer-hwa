using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using PowerRenameAvalonia.Models;
using PowerRenameAvalonia.Services;

namespace PowerRenameAvalonia.Mcp;

public static class McpTools
{
    public static IReadOnlyList<McpTool> Create()
    {
        return new List<McpTool>
        {
            new("scan_wav_assets",
                "지정된 디렉토리 내의 모든 WAV 파일과 BMS 차트 파일(#WAV 정의)의 연동 상태를 스캔하고 분석 리포트를 반환합니다.",
                """
                {
                  "type": "object",
                  "properties": {
                    "directory": {
                      "type": "string",
                      "description": "스캔할 대상 루트 디렉토리 경로 (예: H:\\muse dash hwa\\hwa\\꺾어)"
                    }
                  },
                  "required": ["directory"]
                }
                """,
                HandleScanWavAssetsAsync),

            new("preview_rename",
                "WAV 파일(및 기타 파일)의 일괄 이름 변경 결과를 시뮬레이션하여 미리봅니다. 실제 디스크는 수정되지 않습니다.",
                """
                {
                  "type": "object",
                  "properties": {
                    "directory": { "type": "string", "description": "대상 디렉토리 경로" },
                    "search_text": { "type": "string", "description": "검색할 텍스트 또는 정규식 패턴" },
                    "replace_text": { "type": "string", "description": "대체할 텍스트 (정규식 그룹 $1, $2 사용 가능)" },
                    "use_regex": { "type": "boolean", "description": "정규식 사용 여부 (기본값: false)" },
                    "match_all": { "type": "boolean", "description": "모든 일치 항목 치환 여부 (기본값: true)" },
                    "match_case": { "type": "boolean", "description": "대소문자 구분 여부 (기본값: false)" },
                    "scope": {
                      "type": "string",
                      "enum": ["NameAndExtension", "NameOnly", "ExtensionOnly"],
                      "description": "적용 범위 (기본값: NameAndExtension)"
                    },
                    "formatting": {
                      "type": "string",
                      "enum": ["None", "Lowercase", "Uppercase", "TitleCase", "SentenceCase"],
                      "description": "텍스트 서식 변환 (기본값: None)"
                    },
                    "only_wav": { "type": "boolean", "description": "WAV 파일만 필터링하여 대상 지정 (기본값: true)" },
                    "bms_path": { "type": "string", "description": "연동할 BMS 차트 파일 경로 (생략 시 자동 감지)" }
                  },
                  "required": ["directory", "search_text"]
                }
                """,
                HandlePreviewRenameAsync),

            new("apply_rename",
                "실제 디스크 상의 파일 이름을 일괄 변경하고, 연동된 BMS 차트 파일 내부의 #WAV 정의도 함께 자동 동기화합니다.",
                """
                {
                  "type": "object",
                  "properties": {
                    "directory": { "type": "string", "description": "대상 디렉토리 경로" },
                    "search_text": { "type": "string", "description": "검색할 텍스트 또는 정규식 패턴" },
                    "replace_text": { "type": "string", "description": "대체할 텍스트 (정규식 그룹 $1, $2 사용 가능)" },
                    "use_regex": { "type": "boolean", "description": "정규식 사용 여부 (기본값: false)" },
                    "match_all": { "type": "boolean", "description": "모든 일치 항목 치환 여부 (기본값: true)" },
                    "match_case": { "type": "boolean", "description": "대소문자 구분 여부 (기본값: false)" },
                    "scope": {
                      "type": "string",
                      "enum": ["NameAndExtension", "NameOnly", "ExtensionOnly"],
                      "description": "적용 범위 (기본값: NameAndExtension)"
                    },
                    "formatting": {
                      "type": "string",
                      "enum": ["None", "Lowercase", "Uppercase", "TitleCase", "SentenceCase"],
                      "description": "텍스트 서식 변환 (기본값: None)"
                    },
                    "only_wav": { "type": "boolean", "description": "WAV 파일만 필터링하여 대상 지정 (기본값: true)" },
                    "sync_bms": { "type": "boolean", "description": "BMS 차트 #WAV 정의 동기화 여부 (기본값: true)" },
                    "bms_path": { "type": "string", "description": "연동할 BMS 차트 파일 경로 (생략 시 자동 감지)" }
                  },
                  "required": ["directory", "search_text"]
                }
                """,
                HandleApplyRenameAsync),

            new("sync_bms_chart",
                "BMS 파일 내부의 #WAV 정의들을 제공된 치환 매핑에 따라 직접 동기화하고 백업을 생성합니다.",
                """
                {
                  "type": "object",
                  "properties": {
                    "bms_path": { "type": "string", "description": "대상 BMS 파일 경로" },
                    "replacements": {
                      "type": "object",
                      "description": "치환할 원본 파일명과 새 파일명의 키-값 쌍 (예: {\"old_dt1.wav\": \"new_dt1.wav\"})"
                    }
                  },
                  "required": ["bms_path", "replacements"]
                }
                """,
                HandleSyncBmsChartAsync)
        };
    }

    // 한글 파일명이 \uXXXX 로 이스케이프되지 않도록 완화된 인코더 사용 (가독성·토큰 절약)
    private static readonly JsonSerializerOptions IndentedJson = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private const int PreviewLimit = 30;
    private const int ErrorSampleLimit = 10;

    private static Task<(string text, bool isError)> Ok(JsonObject result)
        => Task.FromResult((result.ToJsonString(IndentedJson), false));

    private static Task<(string text, bool isError)> Fail(string message)
        => Task.FromResult((message, true));

    // 클라이언트가 "true"/"false" 문자열로 보내도 내부 오류로 죽지 않게 관대하게 해석한다.
    private static bool GetBool(JsonObject args, string name, bool defaultValue)
    {
        if (args[name] is not JsonValue value) return defaultValue;
        if (value.TryGetValue<bool>(out var b)) return b;
        if (value.TryGetValue<string>(out var s) && bool.TryParse(s, out var parsed)) return parsed;
        return defaultValue;
    }

    private static string? GetString(JsonObject args, string name)
        => args[name] is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;

    private static TEnum GetEnum<TEnum>(JsonObject args, string name, TEnum defaultValue) where TEnum : struct, Enum
        => Enum.TryParse<TEnum>(GetString(args, name), ignoreCase: true, out var parsed) ? parsed : defaultValue;

    private static JsonArray ToJsonArray(IEnumerable<string> values)
        => new(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());

    private static string[] FindTargetFiles(string dir, bool onlyWav)
    {
        var files = Directory.GetFiles(dir, onlyWav ? "*.wav" : "*", SearchOption.AllDirectories);
        return onlyWav
            ? files.Where(f => Path.GetExtension(f).Equals(".wav", StringComparison.OrdinalIgnoreCase)).ToArray()
            : files;
    }

    private static Task<(string text, bool isError)> HandleScanWavAssetsAsync(JsonObject args, CancellationToken ct)
    {
        string dir = GetString(args, "directory") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            return Fail($"디렉토리를 찾을 수 없습니다: {dir}");

        var wavFiles = FindTargetFiles(dir, onlyWav: true);
        string? bmsFile = BmsSyncService.DetectBmsFile([dir]);

        var report = new JsonObject
        {
            ["directory"] = dir,
            ["total_wav_count"] = wavFiles.Length,
            ["detected_bms_file"] = bmsFile ?? "None"
        };

        if (bmsFile != null)
        {
            try
            {
                string bmsDir = Path.GetDirectoryName(Path.GetFullPath(bmsFile)) ?? dir;
                var bmsWavDefs = BmsSyncService.ReadWavDefinitions(bmsFile);
                report["bms_wav_definitions_count"] = bmsWavDefs.Count;

                var diskFileNames = new HashSet<string>(wavFiles.Select(f => Path.GetFileName(f)), StringComparer.OrdinalIgnoreCase);
                var missingInDisk = bmsWavDefs
                    .Where(w =>
                    {
                        if (w.IndexOfAny(['\\', '/']) >= 0)
                        {
                            string candidate = Path.Combine(bmsDir, w.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
                            return !File.Exists(candidate);
                        }
                        return !diskFileNames.Contains(w);
                    })
                    .ToList();

                report["missing_wav_files_count"] = missingInDisk.Count;
                if (missingInDisk.Count > 0)
                    report["missing_wav_samples"] = ToJsonArray(missingInDisk.Take(ErrorSampleLimit));
            }
            catch (Exception ex)
            {
                report["bms_error"] = ex.Message;
            }
        }

        report["sample_wav_files"] = ToJsonArray(wavFiles.Take(5).Select(p => Path.GetRelativePath(dir, p)));
        return Ok(report);
    }

    private static Task<(string text, bool isError)> HandlePreviewRenameAsync(JsonObject args, CancellationToken ct)
        => ExecuteRenameOperation(args, applyToDisk: false, ct);

    private static Task<(string text, bool isError)> HandleApplyRenameAsync(JsonObject args, CancellationToken ct)
        => ExecuteRenameOperation(args, applyToDisk: true, ct);

    private sealed record PlannedRename(string OldPath, string NewPath)
    {
        public string OldName => Path.GetFileName(OldPath);
        public string NewName => Path.GetFileName(NewPath);
    }

    private static Task<(string text, bool isError)> ExecuteRenameOperation(JsonObject args, bool applyToDisk, CancellationToken ct)
    {
        string dir = GetString(args, "directory") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            return Fail($"디렉토리를 찾을 수 없습니다: {dir}");

        var options = new RenameOptions(
            SearchText: GetString(args, "search_text") ?? string.Empty,
            ReplaceText: GetString(args, "replace_text") ?? string.Empty,
            UseRegex: GetBool(args, "use_regex", false),
            MatchAll: GetBool(args, "match_all", true),
            MatchCase: GetBool(args, "match_case", false),
            Scope: GetEnum(args, "scope", RenameScope.NameAndExtension),
            Formatting: GetEnum(args, "formatting", TextFormatting.None));
        bool onlyWav = GetBool(args, "only_wav", true);
        bool syncBms = GetBool(args, "sync_bms", true);

        // 정규식은 호출당 한 번만 컴파일한다.
        var engine = RenameEngine.Create(options);
        if (engine.Error != null)
            return Fail($"변환 오류: {engine.Error}");

        var files = FindTargetFiles(dir, onlyWav);
        var plans = new List<PlannedRename>();
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            string oldName = Path.GetFileName(file);
            string newName = engine.Transform(oldName, isDirectory: false, out var err);
            if (err != null)
                return Fail($"변환 오류: {err}");
            if (!string.Equals(oldName, newName, StringComparison.Ordinal))
                plans.Add(new PlannedRename(file, Path.Combine(Path.GetDirectoryName(file) ?? dir, newName)));
        }

        // GUI와 동일하게, 같은 대상 경로로 모이는 변경은 충돌로 보고 적용하지 않는다.
        var conflicts = plans
            .GroupBy(p => p.NewPath, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Skip(1).Any())
            .SelectMany(g => g)
            .ToHashSet();

        string? bmsPath = GetString(args, "bms_path");
        if (string.IsNullOrEmpty(bmsPath) || !File.Exists(bmsPath))
            bmsPath = BmsSyncService.DetectBmsFile([dir]);

        var result = new JsonObject
        {
            ["directory"] = dir,
            ["total_scanned_files"] = files.Length,
            ["modified_count"] = plans.Count,
            ["detected_bms_path"] = bmsPath ?? "None",
            ["applied_to_disk"] = applyToDisk
        };

        if (plans.Count > 0)
        {
            result["preview_changes"] = new JsonArray(plans.Take(PreviewLimit).Select(p => (JsonNode)new JsonObject
            {
                ["original"] = p.OldName,
                ["new_name"] = p.NewName,
                ["relative_path"] = Path.GetRelativePath(dir, p.OldPath)
            }).ToArray());
            if (plans.Count > PreviewLimit)
                result["more_changes_omitted"] = plans.Count - PreviewLimit;
        }

        if (conflicts.Count > 0)
        {
            result["conflict_count"] = conflicts.Count;
            result["conflict_samples"] = ToJsonArray(conflicts.Take(ErrorSampleLimit).Select(p => $"{p.OldName} -> {p.NewName}"));
        }

        if (!applyToDisk)
            return Ok(result);

        // 실제 디스크에 적용 (연쇄/교환 리네임도 지원하도록 2단계 일괄 이동 사용)
        var succeeded = new List<RenamedPath>();
        var errors = new List<string>();
        var batchInput = new List<RenamedPath>(plans.Count);

        foreach (var plan in plans)
        {
            if (conflicts.Contains(plan))
                errors.Add($"{plan.OldName}: 다른 파일과 대상 이름이 겹쳐 건너뜀");
            else
                batchInput.Add(new RenamedPath(plan.OldPath, plan.NewPath, IsDirectory: false));
        }

        foreach (var res in FileRenamer.MoveBatch(batchInput))
        {
            if (res.Error == null)
                succeeded.Add(res.Plan);
            else
                errors.Add($"{Path.GetFileName(res.Plan.OldPath)}: {res.Error}");
        }

        result["success_count"] = succeeded.Count;
        result["fail_count"] = errors.Count;
        if (errors.Count > 0)
            result["errors"] = ToJsonArray(errors.Take(ErrorSampleLimit));

        // BMS 동기화: 디스크에서 실제로 성공한 변경만 반영 (실패 항목이 BMS에 들어가 경로가 깨지는 것 방지)
        if (syncBms && bmsPath != null && succeeded.Count > 0)
        {
            result["bms_synced_lines"] = BmsSyncService.SyncBmsFile(bmsPath, succeeded, out var bmsErr);
            if (bmsErr != null)
                result["bms_error"] = bmsErr;
        }

        return Ok(result);
    }

    private static Task<(string text, bool isError)> HandleSyncBmsChartAsync(JsonObject args, CancellationToken ct)
    {
        string bmsPath = GetString(args, "bms_path") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(bmsPath) || !File.Exists(bmsPath))
            return Fail($"BMS 파일을 찾을 수 없습니다: {bmsPath}");

        if (args["replacements"] is not JsonObject replacementsObj || replacementsObj.Count == 0)
            return Fail("치환할 replacements 목록이 비어 있습니다.");

        string bmsDir = Path.GetDirectoryName(Path.GetFullPath(bmsPath)) ?? string.Empty;
        var renames = new List<RenamedPath>();
        foreach (var (oldName, value) in replacementsObj)
        {
            string newName = value is JsonValue v && v.TryGetValue<string>(out var s) ? s : string.Empty;
            if (string.IsNullOrWhiteSpace(newName)) continue;

            // 키는 파일명 또는 BMS 기준 상대경로. 새 이름에 경로 구분자가 있으면 BMS 기준 상대경로로, 없으면 같은 폴더 파일명으로 취급한다.
            string normalizedOld = oldName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            string oldFull = Path.Combine(bmsDir, normalizedOld);
            string newFull = newName.IndexOfAny(['\\', '/']) >= 0
                ? Path.Combine(bmsDir, newName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar))
                : Path.Combine(Path.GetDirectoryName(oldFull) ?? bmsDir, newName);
            renames.Add(new RenamedPath(oldFull, newFull));
        }

        int synced = BmsSyncService.SyncBmsFile(bmsPath, renames, out var err);
        if (err != null)
            return Fail(err);

        var result = new JsonObject
        {
            ["bms_path"] = bmsPath,
            ["synced_lines"] = synced,
            ["backup_created"] = synced > 0 ? bmsPath + ".bak" : "None (변경 없음)",
            ["message"] = $"{synced}개의 #WAV 정의 라인이 성공적으로 동기화되었습니다."
        };

        return Ok(result);
    }
}
