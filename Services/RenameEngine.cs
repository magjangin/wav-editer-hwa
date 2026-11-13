using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using PowerRenameAvalonia.Models;

namespace PowerRenameAvalonia.Services;

/// <summary>
/// 치환 규칙을 한 번만 컴파일해 두고 여러 파일명에 반복 적용하는 변환기.
/// (이전 구현은 파일마다 Regex를 새로 생성해 키 입력마다 N번 파싱했다.)
/// </summary>
public sealed class RenameEngine
{
    // 파국적 백트래킹 패턴이 UI 스레드를 멈추지 않도록 항목당 매치 시간을 제한한다.
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private readonly RenameOptions _options;
    private readonly Regex? _regex;
    private readonly StringComparison _comparison;

    /// <summary>검색 패턴이 잘못되어 변환을 수행할 수 없을 때의 오류 메시지 (정상이면 null).</summary>
    public string? Error { get; }

    private RenameEngine(RenameOptions options, Regex? regex, string? error)
    {
        _options = options;
        _regex = regex;
        Error = error;
        _comparison = options.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
    }

    public static RenameEngine Create(RenameOptions options)
    {
        if (!options.UseRegex || string.IsNullOrEmpty(options.SearchText))
            return new RenameEngine(options, null, null);

        try
        {
            var regexOptions = options.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
            return new RenameEngine(options, new Regex(options.SearchText, regexOptions, RegexTimeout), null);
        }
        catch (ArgumentException ex)
        {
            return new RenameEngine(options, null, $"정규식 오류: {ex.Message}");
        }
    }

    /// <summary>단건 변환 편의 함수 (테스트·일회성 호출용).</summary>
    public static string Transform(string originalFullName, bool isDirectory, RenameOptions options, out string? errorMessage)
        => Create(options).Transform(originalFullName, isDirectory, out errorMessage);

    /// <summary>
    /// 파일/폴더 이름 하나를 변환한다. 오류가 나면 원래 이름을 그대로 돌려준다.
    /// </summary>
    public string Transform(string originalFullName, bool isDirectory, out string? errorMessage)
    {
        errorMessage = Error;
        if (Error != null || string.IsNullOrEmpty(originalFullName))
            return originalFullName;

        // 폴더는 확장명 개념이 없으므로 항상 이름 전체가 대상이다.
        var scope = isDirectory ? RenameScope.NameAndExtension : _options.Scope;
        string extension = scope == RenameScope.NameAndExtension ? string.Empty : Path.GetExtension(originalFullName);
        string stem = originalFullName[..^extension.Length];

        string target = scope == RenameScope.ExtensionOnly ? extension : stem;

        try
        {
            target = Replace(target);
        }
        catch (RegexMatchTimeoutException)
        {
            errorMessage = "정규식 오류: 매칭 시간이 초과되었습니다 (패턴을 단순화하세요).";
            return originalFullName;
        }

        target = ApplyFormatting(target, _options.Formatting);

        return scope switch
        {
            RenameScope.ExtensionOnly => stem + target,
            RenameScope.NameOnly => target + extension,
            _ => target
        };
    }

    private string Replace(string target)
    {
        string search = _options.SearchText;
        if (string.IsNullOrEmpty(search))
            return target;

        string replace = _options.ReplaceText ?? string.Empty;

        if (_regex != null)
            return _options.MatchAll ? _regex.Replace(target, replace) : _regex.Replace(target, replace, 1);

        return _options.MatchAll
            ? ReplaceAll(target, search, replace, _comparison)
            : ReplaceFirst(target, search, replace, _comparison);
    }

    private static string ApplyFormatting(string input, TextFormatting formatting)
    {
        if (string.IsNullOrEmpty(input) || formatting == TextFormatting.None)
            return input;

        var culture = CultureInfo.CurrentCulture;
        return formatting switch
        {
            TextFormatting.Lowercase => input.ToLower(culture),
            TextFormatting.Uppercase => input.ToUpper(culture),
            TextFormatting.TitleCase => culture.TextInfo.ToTitleCase(input),
            TextFormatting.SentenceCase => ToSentenceCase(input, culture),
            _ => input
        };
    }

    private static string ToSentenceCase(string input, CultureInfo culture)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;

        var chars = input.ToCharArray();
        bool isNewSentence = true;

        for (int i = 0; i < chars.Length; i++)
        {
            if (char.IsLetter(chars[i]))
            {
                chars[i] = isNewSentence ? char.ToUpper(chars[i], culture) : char.ToLower(chars[i], culture);
                isNewSentence = false;
            }
            else if (chars[i] is '.' or '!' or '?')
            {
                isNewSentence = true;
            }
        }

        return new string(chars);
    }

    private static string ReplaceAll(string text, string search, string replace, StringComparison comparison)
    {
        int nextMatch = text.IndexOf(search, comparison);
        if (nextMatch < 0)
            return text;

        var sb = new StringBuilder(text.Length);
        int pos = 0;
        while (nextMatch >= 0)
        {
            sb.Append(text, pos, nextMatch - pos).Append(replace);
            pos = nextMatch + search.Length;
            nextMatch = text.IndexOf(search, pos, comparison);
        }
        return sb.Append(text, pos, text.Length - pos).ToString();
    }

    private static string ReplaceFirst(string text, string search, string replace, StringComparison comparison)
    {
        int pos = text.IndexOf(search, comparison);
        return pos < 0 ? text : string.Concat(text.AsSpan(0, pos), replace, text.AsSpan(pos + search.Length));
    }
}
