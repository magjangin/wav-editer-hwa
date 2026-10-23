using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using PowerRenameAvalonia.Models;

namespace PowerRenameAvalonia.Services;

public class RenameEngine
{
    public static string Transform(
        string originalFullName,
        bool isDirectory,
        string searchText,
        string replaceText,
        bool useRegex,
        bool matchAll,
        bool matchCase,
        RenameScope scope,
        TextFormatting formatting,
        out string? errorMessage)
    {
        errorMessage = null;

        if (string.IsNullOrEmpty(originalFullName))
            return originalFullName;

        string namePart;
        string extPart;

        if (isDirectory || scope == RenameScope.NameAndExtension)
        {
            namePart = originalFullName;
            extPart = string.Empty;
        }
        else if (scope == RenameScope.ExtensionOnly)
        {
            extPart = Path.GetExtension(originalFullName);
            namePart = originalFullName[..^extPart.Length];
        }
        else // NameOnly
        {
            extPart = Path.GetExtension(originalFullName);
            namePart = Path.GetFileNameWithoutExtension(originalFullName);
        }

        string target = scope switch
        {
            RenameScope.ExtensionOnly => extPart,
            _ => namePart
        };

        // 1. 치환 (Search & Replace)
        if (!string.IsNullOrEmpty(searchText))
        {
            try
            {
                if (useRegex)
                {
                    var options = RegexOptions.None;
                    if (!matchCase)
                    {
                        options |= RegexOptions.IgnoreCase;
                    }

                    var regex = new Regex(searchText, options);
                    if (matchAll)
                    {
                        target = regex.Replace(target, replaceText ?? string.Empty);
                    }
                    else
                    {
                        target = regex.Replace(target, replaceText ?? string.Empty, 1);
                    }
                }
                else
                {
                    var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                    if (matchAll)
                    {
                        target = ReplaceAll(target, searchText, replaceText ?? string.Empty, comparison);
                    }
                    else
                    {
                        target = ReplaceFirst(target, searchText, replaceText ?? string.Empty, comparison);
                    }
                }
            }
            catch (Exception ex)
            {
                errorMessage = $"정규식 오류: {ex.Message}";
                return originalFullName;
            }
        }

        // 2. 텍스트 서식 지정 (Formatting)
        target = ApplyFormatting(target, formatting);

        // 3. 결합
        if (isDirectory || scope == RenameScope.NameAndExtension)
        {
            return target;
        }
        else if (scope == RenameScope.ExtensionOnly)
        {
            return namePart + target;
        }
        else // NameOnly
        {
            return target + extPart;
        }
    }

    private static string ApplyFormatting(string input, TextFormatting formatting)
    {
        if (string.IsNullOrEmpty(input) || formatting == TextFormatting.None)
            return input;

        return formatting switch
        {
            TextFormatting.Lowercase => input.ToLower(CultureInfo.CurrentCulture),
            TextFormatting.Uppercase => input.ToUpper(CultureInfo.CurrentCulture),
            TextFormatting.TitleCase => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(input),
            TextFormatting.SentenceCase => ToSentenceCase(input),
            _ => input
        };
    }

    private static string ToSentenceCase(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;

        var chars = input.ToCharArray();
        bool isNewSentence = true;

        for (int i = 0; i < chars.Length; i++)
        {
            if (char.IsLetter(chars[i]))
            {
                if (isNewSentence)
                {
                    chars[i] = char.ToUpper(chars[i], CultureInfo.CurrentCulture);
                    isNewSentence = false;
                }
                else
                {
                    chars[i] = char.ToLower(chars[i], CultureInfo.CurrentCulture);
                }
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
        if (string.IsNullOrEmpty(search))
            return text;

        int pos = 0;
        int nextMatch = text.IndexOf(search, pos, comparison);
        if (nextMatch == -1)
            return text;

        var sb = new System.Text.StringBuilder();
        while (nextMatch != -1)
        {
            sb.Append(text, pos, nextMatch - pos);
            sb.Append(replace);
            pos = nextMatch + search.Length;
            nextMatch = text.IndexOf(search, pos, comparison);
        }
        sb.Append(text, pos, text.Length - pos);
        return sb.ToString();
    }

    private static string ReplaceFirst(string text, string search, string replace, StringComparison comparison)
    {
        int pos = text.IndexOf(search, comparison);
        if (pos < 0)
            return text;

        return text[..pos] + replace + text[(pos + search.Length)..];
    }
}
