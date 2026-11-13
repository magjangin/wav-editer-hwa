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
    /// </summary>
    public static string? DetectBmsFile(IEnumerable<string> paths)
    {
        var directories = paths
            .Select(p => File.Exists(p) ? Path.GetDirectoryName(p) : (Directory.Exists(p) ? p : null))
            .OfType<string>()
            .Where(d => d.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in directories)
        {
            var found = FirstBmsIn(dir) ?? (Directory.GetParent(dir) is { Exists: true } parent ? FirstBmsIn(parent.FullName) : null);
            if (found != null) return found;
        }

        return null;
    }

    private static string? FirstBmsIn(string dir)
        => Directory.EnumerateFiles(dir, "*.bms", SearchOption.TopDirectoryOnly).FirstOrDefault();

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
            var (nameMap, relativeMap) = BuildMaps(bmsFilePath, renames);
            if (nameMap.Count == 0) return 0;

            byte[] original = File.ReadAllBytes(bmsFilePath);
            var (text, encoding) = ReadText(original);

            int replacedCount = 0;
            string updated = WavDefinitionRegex.Replace(text, m =>
            {
                string wavPath = m.Groups[2].Value;
                string? newPath = null;

                // 1. 상대경로 매칭 우선
                if (relativeMap.TryGetValue(wavPath, out var newRel))
                {
                    newPath = newRel;
                }
                // 2. 파일명 매칭 (BMS 안의 폴더 부분과 구분자는 원문 그대로 유지)
                else if (nameMap.TryGetValue(GetFileName(wavPath), out var newFileName))
                {
                    newPath = wavPath[..^GetFileName(wavPath).Length] + newFileName;
                }

                if (newPath == null || newPath == wavPath) return m.Value;
                replacedCount++;
                return m.Groups[1].Value + newPath;
            });

            if (replacedCount > 0)
            {
                File.WriteAllBytes(bmsFilePath + ".bak", original);
                File.WriteAllBytes(bmsFilePath, Encode(updated, encoding));
            }

            return replacedCount;
        }
        catch (Exception ex)
        {
            errorMessage = $"BMS 갱신 중 오류: {ex.Message}";
            return 0;
        }
    }

    private static (Dictionary<string, string> nameMap, Dictionary<string, string> relativeMap) BuildMaps(
        string bmsFilePath, IEnumerable<RenamedPath> renames)
    {
        string bmsDir = Path.GetDirectoryName(Path.GetFullPath(bmsFilePath)) ?? string.Empty;
        var nameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var relativeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (oldPath, newPath, isDirectory) in renames)
        {
            if (isDirectory) continue;

            string oldName = Path.GetFileName(oldPath);
            string newName = Path.GetFileName(newPath);
            if (string.Equals(oldName, newName, StringComparison.Ordinal)) continue;

            nameMap[oldName] = newName;

            // BMS 폴더 하위인지 경로 경계 기준으로 판정 ("C:\foo"가 "C:\foobar"를 포함한다고 보지 않음)
            string oldRel = Path.GetRelativePath(bmsDir, Path.GetFullPath(oldPath));
            if (oldRel.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(oldRel)) continue;

            string newRel = Path.Combine(Path.GetDirectoryName(oldRel) ?? string.Empty, newName);
            relativeMap[oldRel.Replace('/', '\\')] = newRel.Replace('/', '\\');
            relativeMap[oldRel.Replace('\\', '/')] = newRel.Replace('\\', '/');
        }

        return (nameMap, relativeMap);
    }

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
