using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using PowerRenameAvalonia.ViewModels;

namespace PowerRenameAvalonia.Services;

public static class BmsSyncService
{
    // #WAV01 또는 #WAV001 과 같은 헤더 패턴
    private static readonly Regex WavHeaderRegex = new(@"^(#WAV[0-9A-Za-z]{2,3})\s+(.+)$", RegexOptions.Compiled);

    /// <summary>
    /// 지정된 디렉토리 또는 그 상위/하위 디렉토리에서 .bms 파일을 자동 탐색합니다.
    /// </summary>
    public static string? DetectBmsFile(IEnumerable<string> filePaths)
    {
        var directories = filePaths
            .Select(p => File.Exists(p) ? Path.GetDirectoryName(p) : (Directory.Exists(p) ? p : null))
            .Where(d => !string.IsNullOrEmpty(d))
            .Distinct()
            .ToList();

        foreach (var dir in directories)
        {
            if (dir == null) continue;

            // 1. 해당 폴더 내 .bms 파일
            var bmsFiles = Directory.GetFiles(dir, "*.bms", SearchOption.TopDirectoryOnly);
            if (bmsFiles.Length > 0) return bmsFiles[0];

            // 2. 부모 폴더 내 .bms 파일
            var parent = Directory.GetParent(dir);
            if (parent != null && parent.Exists)
            {
                var parentBms = Directory.GetFiles(parent.FullName, "*.bms", SearchOption.TopDirectoryOnly);
                if (parentBms.Length > 0) return parentBms[0];
            }
        }

        return null;
    }

    /// <summary>
    /// BMS 파일 내부의 #WAV 정의들을 변경된 파일명으로 동기화 갱신합니다.
    /// </summary>
    public static int SyncBmsFile(string bmsFilePath, IReadOnlyList<RenameItemViewModel> renamedItems, out string? errorMessage)
    {
        errorMessage = null;

        if (!File.Exists(bmsFilePath))
        {
            errorMessage = "BMS 파일을 찾을 수 없습니다: " + bmsFilePath;
            return 0;
        }

        try
        {
            // 변경 매핑 생성 (파일명 기준 및 상대경로 기준)
            var bmsDir = Path.GetDirectoryName(bmsFilePath) ?? string.Empty;

            var nameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var relativeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in renamedItems)
            {
                if (!item.IsModified) continue;

                nameMap[item.OriginalName] = item.NewName;

                if (!string.IsNullOrEmpty(bmsDir) && item.FullPath.StartsWith(bmsDir, StringComparison.OrdinalIgnoreCase))
                {
                    string oldRel = Path.GetRelativePath(bmsDir, item.FullPath);
                    string newRel = Path.Combine(Path.GetDirectoryName(oldRel) ?? string.Empty, item.NewName);

                    relativeMap[oldRel.Replace('/', '\\')] = newRel.Replace('/', '\\');
                    relativeMap[oldRel.Replace('\\', '/')] = newRel.Replace('\\', '/');
                }
            }

            if (nameMap.Count == 0) return 0;

            // 자동 백업 생성 (.bms.bak)
            string backupPath = bmsFilePath + ".bak";
            File.Copy(bmsFilePath, backupPath, true);

            // BMS 읽기 (UTF-8 인코딩)
            var encoding = DetectEncoding(bmsFilePath);
            var lines = File.ReadAllLines(bmsFilePath, encoding);
            int replacedCount = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                var match = WavHeaderRegex.Match(lines[i]);
                if (match.Success)
                {
                    string headerKey = match.Groups[1].Value;
                    string wavPath = match.Groups[2].Value.Trim();

                    string wavFileName = Path.GetFileName(wavPath);
                    string wavDir = Path.GetDirectoryName(wavPath) ?? string.Empty;

                    // 1. 상대경로 매칭 우선
                    if (relativeMap.TryGetValue(wavPath, out var newRelPath))
                    {
                        lines[i] = $"{headerKey} {newRelPath}";
                        replacedCount++;
                    }
                    // 2. 파일명 매칭
                    else if (nameMap.TryGetValue(wavFileName, out var newFileName))
                    {
                        string newPath = string.IsNullOrEmpty(wavDir) ? newFileName : Path.Combine(wavDir, newFileName);
                        lines[i] = $"{headerKey} {newPath}";
                        replacedCount++;
                    }
                }
            }

            if (replacedCount > 0)
            {
                File.WriteAllLines(bmsFilePath, lines, encoding);
            }

            return replacedCount;
        }
        catch (Exception ex)
        {
            errorMessage = $"BMS 갱신 중 오류: {ex.Message}";
            return 0;
        }
    }

    private static Encoding DetectEncoding(string filePath)
    {
        // BOM 확인
        byte[] buffer = new byte[4];
        using (var file = new FileStream(filePath, FileMode.Open, FileAccess.Read))
        {
            int read = file.Read(buffer, 0, 4);
            if (read >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
            {
                return new UTF8Encoding(true);
            }
        }

        // 기본 UTF-8 (BOM 없음)
        return new UTF8Encoding(false);
    }
}
