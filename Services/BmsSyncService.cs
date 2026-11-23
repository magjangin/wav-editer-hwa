using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using PowerRenameAvalonia.Models;

namespace PowerRenameAvalonia.Services;

public static class BmsSyncService
{
    // #WAV01 / #WAV001 정의 행. 1그룹 = "#WAVxx" + 구분 공백(원문 그대로 보존), 2그룹 = 경로.
    // 행 끝의 공백·\r 은 경로에 포함하지 않아 원래 줄바꿈(LF/CRLF)이 그대로 유지된다.
    private static readonly Regex WavDefinitionRegex = new(
        @"^(#WAV[0-9A-Za-z]{2,3}[ \t]+)(.+?)[ \t]*(?=\r?$)",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static BmsSyncService()
    {
        // CP949/Shift-JIS 등 ANSI 코드 페이지 BMS를 손상 없이 다루기 위해 등록.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// 지정된 경로(파일이면 그 폴더)와 상위 폴더에서 .bms 파일을 자동 탐색합니다.
    /// 같은 폴더에 파일이 수천 개 들어와도 폴더별로 한 번씩만 검사합니다.
    /// </summary>
    public static string? DetectBmsFile(IEnumerable<string> paths)
    {
        var candidateDirs = new List<string>();
        var seenCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;

            // 디렉토리인지 확인하기 전에 이미 부모 경로로 추가된 일반 파일은 건너뛰어 I/O 호출을 줄인다.
            string? dir = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && seenCandidates.Add(dir))
                candidateDirs.Add(dir);
        }

        var scannedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in candidateDirs)
        {
            if (scannedDirs.Add(dir))
            {
                var found = FirstBmsIn(dir);
                if (found != null) return found;
            }

            if (Directory.GetParent(dir) is { Exists: true } parent && scannedDirs.Add(parent.FullName))
            {
                var found = FirstBmsIn(parent.FullName);
                if (found != null) return found;
            }
        }

        return null;
    }

    private static string? FirstBmsIn(string dir)
    {
        try
        {
            // 이름순으로 고정해 파일 시스템 열거 순서에 따라 결과가 흔들리지 않게 한다.
            return Directory.EnumerateFiles(dir, "*.bms", FileScanner.Options(false))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null; // 상위 폴더 접근 불가 등은 "BMS 없음"으로 취급
        }
    }

    /// <summary>BMS 파일의 #WAV 정의 경로 목록을 읽습니다 (인코딩 자동 감지).</summary>
    public static List<string> ReadWavDefinitions(string bmsFilePath)
    {
        var (text, _) = ReadText(File.ReadAllBytes(bmsFilePath));
        return WavDefinitionRegex.Matches(text).Select(m => m.Groups[2].Value).ToList();
    }

    /// <summary>
    /// 실제로 성공한 이름 변경 목록을 받아 BMS 내부의 #WAV 정의를 갱신합니다.
    /// 변경된 행이 있을 때만 <c>.bms.bak</c> 백업을 만들고 원본 인코딩·BOM·줄바꿈을 유지해 저장합니다.
    /// </summary>
    /// <returns>갱신된 #WAV 행 수</returns>
    public static int SyncBmsFile(string bmsFilePath, IReadOnlyCollection<RenamedPath> renames, out string? errorMessage)
    {
        errorMessage = null;

        if (!File.Exists(bmsFilePath))
        {
            errorMessage = "BMS 파일을 찾을 수 없습니다: " + bmsFilePath;
            return 0;
        }

        try
        {
            var (nameMap, relativeMap, dirLeafMap, bmsDir) = BuildMaps(bmsFilePath, renames);
            if (nameMap.Count == 0 && relativeMap.Count == 0 && dirLeafMap.Count == 0) return 0;

            byte[] original = File.ReadAllBytes(bmsFilePath);
            var (text, encoding) = ReadText(original);

            int replacedCount = 0;
            string updated = WavDefinitionRegex.Replace(text, m =>
            {
                string wavPath = m.Groups[2].Value;
                string newPath = ResolveUpdatedWavPath(wavPath, bmsDir, nameMap, relativeMap, dirLeafMap);

                if (string.Equals(newPath, wavPath, StringComparison.Ordinal)) return m.Value;
                replacedCount++;
                return m.Groups[1].Value + newPath;
            });

            if (replacedCount > 0)
            {
                File.WriteAllBytes(bmsFilePath + ".bak", original);

                // 임시 파일에 먼저 쓴 뒤 교체해, 쓰는 도중 중단돼도 BMS가 반쯤 잘린 채 남지 않게 한다.
                string tempPath = bmsFilePath + ".tmp";
                File.WriteAllBytes(tempPath, Encode(updated, encoding));
                File.Move(tempPath, bmsFilePath, overwrite: true);
            }

            return replacedCount;
        }
        catch (Exception ex)
        {
            try { File.Delete(bmsFilePath + ".tmp"); } catch { /* 정리 실패는 무시 */ }
            errorMessage = $"BMS 갱신 중 오류: {ex.Message}";
            return 0;
        }
    }

    private static string ResolveUpdatedWavPath(
        string wavPath,
        string bmsDir,
        Dictionary<string, string> nameMap,
        Dictionary<string, string> relativeMap,
        Dictionary<string, string> dirLeafMap)
    {
        // 1. 폴더 이름 변경이 있으면 각 경로 세그먼트를 원래 구분자를 유지한 채 갱신한다.
        string afterDir = RewriteDirectorySegments(wavPath, dirLeafMap, out bool dirChanged);

        // 2. 상대경로 매칭 우선 (폴더도 함께 바뀐 경우 afterDir의 폴더에 새 파일명을 결합)
        if (relativeMap.TryGetValue(wavPath, out var newRel))
            return dirChanged ? ReplaceFileName(afterDir, GetFileName(newRel)) : newRel;

        // 3. 파일명 매칭 폴백: 다른 씬 폴더의 파일이 디스크에 그대로 존재하면 동명 파일 오매칭을 막는다.
        string fileName = GetFileName(afterDir);
        if (nameMap.TryGetValue(fileName, out var newFileName))
        {
            bool hasDir = afterDir.IndexOfAny(['\\', '/']) >= 0;
            if (!hasDir || !File.Exists(ToDiskPath(bmsDir, afterDir)))
                return ReplaceFileName(afterDir, newFileName);
        }

        return afterDir;
    }

    private static (
        Dictionary<string, string> nameMap,
        Dictionary<string, string> relativeMap,
        Dictionary<string, string> dirLeafMap,
        string bmsDir) BuildMaps(string bmsFilePath, IEnumerable<RenamedPath> renames)
    {
        string bmsDir = Path.GetDirectoryName(Path.GetFullPath(bmsFilePath)) ?? string.Empty;
        var nameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ambiguousNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var relativeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var dirLeafMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (oldPath, newPath, isDirectory) in renames)
        {
            string oldFull = Path.GetFullPath(oldPath);
            string newFull = Path.GetFullPath(newPath);
            string oldRel = Path.GetRelativePath(bmsDir, oldFull);

            // BMS 폴더 하위인지 경로 경계 기준으로 판정 ("C:\foo"가 "C:\foobar"를 포함한다고 보지 않음)
            bool isUnderBmsDir = !oldRel.StartsWith("..", StringComparison.Ordinal)
                                 && !Path.IsPathRooted(oldRel)
                                 && oldRel != ".";

            if (isDirectory)
            {
                if (!isUnderBmsDir) continue;
                string newRel = Path.GetRelativePath(bmsDir, newFull);
                if (newRel.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(newRel)) continue;

                string oldClean = Path.TrimEndingDirectorySeparator(oldRel).Replace('\\', '/');
                string newLeaf = Path.GetFileName(Path.TrimEndingDirectorySeparator(newFull));
                if (!string.Equals(Path.GetFileName(oldClean), newLeaf, StringComparison.Ordinal))
                    dirLeafMap[oldClean] = newLeaf;
                continue;
            }

            string oldName = Path.GetFileName(oldFull);
            string newName = Path.GetFileName(newFull);
            if (!string.Equals(oldName, newName, StringComparison.Ordinal) && !ambiguousNames.Contains(oldName))
            {
                if (nameMap.TryGetValue(oldName, out var existingNewName))
                {
                    // 서로 다른 폴더의 동명 파일이 각기 다른 이름으로 바뀌었으면 파일명만으로는 모호하므로 제외한다.
                    if (!string.Equals(existingNewName, newName, StringComparison.Ordinal))
                    {
                        nameMap.Remove(oldName);
                        ambiguousNames.Add(oldName);
                    }
                }
                else
                {
                    nameMap[oldName] = newName;
                }
            }

            if (!isUnderBmsDir) continue;
            string fileNewRel = Path.GetRelativePath(bmsDir, newFull);
            if (fileNewRel.StartsWith("..", StringComparison.Ordinal)
                || Path.IsPathRooted(fileNewRel)
                || string.Equals(oldRel, fileNewRel, StringComparison.Ordinal))
                continue;

            relativeMap[oldRel.Replace('/', '\\')] = fileNewRel.Replace('/', '\\');
            relativeMap[oldRel.Replace('\\', '/')] = fileNewRel.Replace('\\', '/');
        }

        return (nameMap, relativeMap, dirLeafMap, bmsDir);
    }

    private static string RewriteDirectorySegments(string wavPath, Dictionary<string, string> dirLeafMap, out bool dirChanged)
    {
        dirChanged = false;
        if (dirLeafMap.Count == 0) return wavPath;

        int lastSep = wavPath.LastIndexOfAny(['\\', '/']);
        if (lastSep < 0) return wavPath;

        var sb = new StringBuilder(wavPath.Length);
        string cumulative = string.Empty;
        int pos = 0;

        while (pos <= lastSep)
        {
            int sep = wavPath.IndexOfAny(['\\', '/'], pos);
            if (sep < 0 || sep > lastSep) break;

            string seg = wavPath[pos..sep];
            if (seg.Length > 0 && seg != ".")
            {
                cumulative = cumulative.Length == 0 ? seg : $"{cumulative}/{seg}";
                if (dirLeafMap.TryGetValue(cumulative, out var newLeaf))
                {
                    sb.Append(newLeaf);
                    dirChanged = true;
                }
                else
                {
                    sb.Append(seg);
                }
            }
            else
            {
                sb.Append(seg);
            }

            sb.Append(wavPath[sep]);
            pos = sep + 1;
        }

        if (!dirChanged) return wavPath;
        sb.Append(wavPath, lastSep + 1, wavPath.Length - (lastSep + 1));
        return sb.ToString();
    }

    private static string ReplaceFileName(string bmsPath, string newFileName)
    {
        int idx = bmsPath.LastIndexOfAny(['\\', '/']);
        return idx < 0 ? newFileName : string.Concat(bmsPath.AsSpan(0, idx + 1), newFileName);
    }

    private static string ToDiskPath(string bmsDir, string bmsRelPath)
        => Path.Combine(bmsDir, bmsRelPath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));

    // BMS 경로는 OS와 무관하게 '\' 와 '/' 를 모두 구분자로 취급한다.
    private static string GetFileName(string bmsPath)
    {
        int idx = bmsPath.LastIndexOfAny(['\\', '/']);
        return idx < 0 ? bmsPath : bmsPath[(idx + 1)..];
    }

    /// <summary>
    /// UTF-8(BOM 유무) 우선, 유효하지 않으면 시스템 ANSI 코드 페이지(한국어 Windows = CP949)로 해석한다.
    /// ANSI 해석이 원본 바이트로 정확히 되돌아가지 않으면 손상을 막기 위해 예외를 던진다.
    /// </summary>
    internal static (string text, Encoding encoding) ReadText(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return (new UTF8Encoding(false, true).GetString(bytes, 3, bytes.Length - 3), new UTF8Encoding(true));

        try
        {
            return (new UTF8Encoding(false, true).GetString(bytes), new UTF8Encoding(false));
        }
        catch (DecoderFallbackException)
        {
            var ansi = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage,
                EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            string text = ansi.GetString(bytes);
            if (!ansi.GetBytes(text).AsSpan().SequenceEqual(bytes))
                throw new InvalidDataException($"BMS 인코딩을 판별할 수 없어 손상 방지를 위해 수정하지 않았습니다 (UTF-8/{ansi.WebName} 아님).");
            return (text, ansi);
        }
    }

    private static byte[] Encode(string text, Encoding encoding)
    {
        byte[] body = encoding.GetBytes(text);
        byte[] preamble = encoding.GetPreamble();
        return preamble.Length == 0 ? body : [.. preamble, .. body];
    }
}
