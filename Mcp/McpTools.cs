using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using PowerRenameAvalonia.Models;
using PowerRenameAvalonia.Services;
using PowerRenameAvalonia.ViewModels;

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

    private static Task<(string text, bool isError)> HandleScanWavAssetsAsync(JsonObject args, CancellationToken ct)
    {
        string dir = args["directory"]?.GetValue<string>() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        {
            return Task.FromResult(($"디렉토리를 찾을 수 없습니다: {dir}", true));
        }

        var wavFiles = Directory.GetFiles(dir, "*.wav", SearchOption.AllDirectories);
        string? bmsFile = BmsSyncService.DetectBmsFile(new[] { dir });

        var report = new JsonObject
        {
            ["directory"] = dir,
            ["total_wav_count"] = wavFiles.Length,
            ["detected_bms_file"] = bmsFile ?? "None"
        };

        if (!string.IsNullOrEmpty(bmsFile) && File.Exists(bmsFile))
        {
            var bmsLines = File.ReadAllLines(bmsFile);
            var wavDefRegex = new Regex(@"^#WAV[0-9A-Za-z]{2,3}\s+(.+)$");
            var bmsWavDefs = new List<string>();

            foreach (var line in bmsLines)
            {
                var match = wavDefRegex.Match(line);
                if (match.Success)
                {
                    bmsWavDefs.Add(match.Groups[1].Value.Trim());
                }
            }

            report["bms_wav_definitions_count"] = bmsWavDefs.Count;

            var diskFileNames = new HashSet<string>(wavFiles.Select(Path.GetFileName)!, StringComparer.OrdinalIgnoreCase);
            var missingInDisk = bmsWavDefs.Where(w => !diskFileNames.Contains(Path.GetFileName(w))).ToList();

            report["missing_wav_files_count"] = missingInDisk.Count;
            if (missingInDisk.Count > 0)
            {
                report["missing_wav_samples"] = new JsonArray(missingInDisk.Take(10).Select(x => (JsonNode)JsonValue.Create(x)!).ToArray());
            }
        }

        // 샘플 WAV 파일 5개 표시
        report["sample_wav_files"] = new JsonArray(wavFiles.Take(5).Select(p => (JsonNode)JsonValue.Create(Path.GetRelativePath(dir, p))!).ToArray());

        return Task.FromResult((report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), false));
    }

    private static Task<(string text, bool isError)> HandlePreviewRenameAsync(JsonObject args, CancellationToken ct)
    {
        return ExecuteRenameOperation(args, applyToDisk: false);
    }

    private static Task<(string text, bool isError)> HandleApplyRenameAsync(JsonObject args, CancellationToken ct)
    {
        return ExecuteRenameOperation(args, applyToDisk: true);
    }

    private static Task<(string text, bool isError)> ExecuteRenameOperation(JsonObject args, bool applyToDisk)
    {
        string dir = args["directory"]?.GetValue<string>() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        {
            return Task.FromResult(($"디렉토리를 찾을 수 없습니다: {dir}", true));
        }

        string searchText = args["search_text"]?.GetValue<string>() ?? string.Empty;
        string replaceText = args["replace_text"]?.GetValue<string>() ?? string.Empty;
        bool useRegex = args["use_regex"]?.GetValue<bool>() ?? false;
        bool matchAll = args["match_all"]?.GetValue<bool>() ?? true;
        bool matchCase = args["match_case"]?.GetValue<bool>() ?? false;
        bool onlyWav = args["only_wav"]?.GetValue<bool>() ?? true;
        bool syncBms = args["sync_bms"]?.GetValue<bool>() ?? true;
        string? bmsPath = args["bms_path"]?.GetValue<string>();

        RenameScope scope = RenameScope.NameAndExtension;
        if (args["scope"]?.GetValue<string>() is string scopeStr && Enum.TryParse<RenameScope>(scopeStr, out var parsedScope))
        {
            scope = parsedScope;
        }

        TextFormatting formatting = TextFormatting.None;
        if (args["formatting"]?.GetValue<string>() is string formatStr && Enum.TryParse<TextFormatting>(formatStr, out var parsedFormat))
        {
            formatting = parsedFormat;
        }

        // 대상 파일 목록 검색
        string pattern = onlyWav ? "*.wav" : "*";
        var files = Directory.GetFiles(dir, pattern, SearchOption.AllDirectories);
        if (onlyWav)
        {
            files = files.Where(f => Path.GetExtension(f).Equals(".wav", StringComparison.OrdinalIgnoreCase)).ToArray();
        }

        var items = new List<RenameItemViewModel>();
        foreach (var file in files)
        {
            items.Add(new RenameItemViewModel(file, false));
        }

        // 변환 계산
        var modifiedList = new List<JsonObject>();
        string? transformError = null;

        foreach (var item in items)
        {
            string newName = RenameEngine.Transform(
                item.OriginalName,
                false,
                searchText,
                replaceText,
                useRegex,
                matchAll,
                matchCase,
                scope,
                formatting,
                out var err
            );

            if (err != null && transformError == null) transformError = err;

            item.NewName = newName;
            if (item.IsModified)
            {
                modifiedList.Add(new JsonObject
                {
                    ["original"] = item.OriginalName,
                    ["new_name"] = item.NewName,
                    ["relative_path"] = Path.GetRelativePath(dir, item.FullPath)
                });
            }
        }

        if (transformError != null)
        {
            return Task.FromResult(($"변환 오류: {transformError}", true));
        }

        // BMS 파일 경로 결정
        if (string.IsNullOrEmpty(bmsPath) || !File.Exists(bmsPath))
        {
            bmsPath = BmsSyncService.DetectBmsFile(new[] { dir });
        }

        var result = new JsonObject
        {
            ["directory"] = dir,
            ["total_scanned_files"] = files.Length,
            ["modified_count"] = modifiedList.Count,
            ["detected_bms_path"] = bmsPath ?? "None",
            ["applied_to_disk"] = applyToDisk
        };

        if (modifiedList.Count > 0)
        {
            result["preview_changes"] = new JsonArray(modifiedList.Take(30).Select(x => (JsonNode)x.DeepClone()).ToArray());
            if (modifiedList.Count > 30)
            {
                result["more_changes_omitted"] = modifiedList.Count - 30;
            }
        }

        if (!applyToDisk)
        {
            return Task.FromResult((result.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), false));
        }

        // 실제 디스크에 적용
        int successCount = 0;
        int failCount = 0;
        var errors = new List<string>();

        foreach (var item in items.Where(x => x.IsModified))
        {
            try
            {
                string oldPath = item.FullPath;
                string newPath = Path.Combine(item.DirectoryPath, item.NewName);

                if (string.Equals(oldPath, newPath, StringComparison.Ordinal)) continue;

                if (string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
                {
                    string tempPath = Path.Combine(item.DirectoryPath, $"__pr_temp_{Guid.NewGuid():N}");
                    File.Move(oldPath, tempPath);
                    File.Move(tempPath, newPath);
                }
                else
                {
                    if (File.Exists(newPath))
                    {
                        throw new IOException($"대상 파일이 이미 존재합니다: {item.NewName}");
                    }
                    File.Move(oldPath, newPath);
                }

                item.FullPath = newPath;
                successCount++;
            }
            catch (Exception ex)
            {
                failCount++;
                errors.Add($"{item.OriginalName}: {ex.Message}");
            }
        }

        result["success_count"] = successCount;
        result["fail_count"] = failCount;
        if (errors.Count > 0)
        {
            result["errors"] = new JsonArray(errors.Take(10).Select(e => (JsonNode)JsonValue.Create(e)!).ToArray());
        }

        // BMS 파일 동기화
        if (syncBms && !string.IsNullOrEmpty(bmsPath) && File.Exists(bmsPath))
        {
            int bmsSynced = BmsSyncService.SyncBmsFile(bmsPath, items, out var bmsErr);
            result["bms_synced_lines"] = bmsSynced;
            if (bmsErr != null)
            {
                result["bms_error"] = bmsErr;
            }
        }

        return Task.FromResult((result.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), false));
    }

    private static Task<(string text, bool isError)> HandleSyncBmsChartAsync(JsonObject args, CancellationToken ct)
    {
        string bmsPath = args["bms_path"]?.GetValue<string>() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(bmsPath) || !File.Exists(bmsPath))
        {
            return Task.FromResult(($"BMS 파일을 찾을 수 없습니다: {bmsPath}", true));
        }

        var replacementsObj = args["replacements"] as JsonObject;
        if (replacementsObj == null || replacementsObj.Count == 0)
        {
            return Task.FromResult(("치환할 replacements 목록이 비어 있습니다.", true));
        }

        var items = new List<RenameItemViewModel>();
        string bmsDir = Path.GetDirectoryName(bmsPath) ?? string.Empty;

        foreach (var kvp in replacementsObj)
        {
            string oldName = kvp.Key;
            string newName = kvp.Value?.GetValue<string>() ?? string.Empty;

            var vm = new RenameItemViewModel(Path.Combine(bmsDir, oldName), false)
            {
                NewName = newName
            };
            items.Add(vm);
        }

        int synced = BmsSyncService.SyncBmsFile(bmsPath, items, out var err);
        if (err != null)
        {
            return Task.FromResult((err, true));
        }

        var result = new JsonObject
        {
            ["bms_path"] = bmsPath,
            ["synced_lines"] = synced,
            ["backup_created"] = bmsPath + ".bak",
            ["message"] = $"{synced}개의 #WAV 정의 라인이 성공적으로 동기화되었습니다."
        };

        return Task.FromResult((result.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), false));
    }
}
