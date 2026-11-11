using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ExcelRenderer.OpenXml.Reader.Formatting;

/// <summary>Resolves named colors from the value-selected number-format section.</summary>
internal static class NumberFormatColor
{
    /// <summary>Resolves one of the eight named Excel colors for a valid format.</summary>
    /// <param name="code">The validated format code.</param>
    /// <param name="number">Numeric value, or null for text.</param>
    /// <returns>RGB hex digits, or null when no named color applies.</returns>
    internal static string? Resolve(string code, double? number)
    {
        if (code.IndexOf('[') < 0)
        {
            return null;
        }

        var sections = new List<string>();
        var section = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < code.Length; i++)
        {
            var c = code[i];
            if (!quoted && (c == '\\' || c == '_' || c == '*') && i + 1 < code.Length)
            {
                section.Append(c).Append(code[++i]);
            }
            else if (c == '"')
            {
                quoted = !quoted;
                section.Append(c);
            }
            else if (c == ';' && !quoted)
            {
                sections.Add(section.ToString());
                section.Clear();
            }
            else
            {
                section.Append(c);
            }
        }

        sections.Add(section.ToString());
        if (!number.HasValue)
        {
            return sections.Count == 4 ? Color(sections[3]) : null;
        }

        var value = number.Value;
        var first = Condition(sections[0], value);
        var second = sections.Count > 1 ? Condition(sections[1], value) : null;
        if (first ?? (sections.Count == 1 || (sections.Count == 2 && value >= 0) || (sections.Count >= 2 && value > 0)))
        {
            return Color(sections[0]);
        }

        if (sections.Count < 2)
        {
            return null;
        }

        if (second ?? (value < 0 || (sections.Count == 2 && first.HasValue)))
        {
            return Color(sections[1]);
        }

        return sections.Count > 2 ? Color(sections[2]) : null;
    }

    private static IEnumerable<string> Tags(string section)
    {
        for (var i = 0; i < section.Length && section[i] == '[';)
        {
            var end = section.IndexOf(']', i + 1);
            if (end < 0)
            {
                yield break;
            }

            yield return section.Substring(i + 1, end - i - 1);
            i = end + 1;
        }
    }

    private static bool? Condition(string section, double value)
    {
        foreach (var tag in Tags(section))
        {
            var length = 0;
            while (length < tag.Length && (tag[length] == '<' || tag[length] == '>' || tag[length] == '='))
            {
                length++;
            }

            if (length == 0 || !double.TryParse(tag.Substring(length), NumberStyles.Float, CultureInfo.InvariantCulture, out var threshold))
            {
                continue;
            }

            return tag.Substring(0, length) switch
            {
                "<" => value < threshold,
                "<=" => value <= threshold,
                ">" => value > threshold,
                ">=" => value >= threshold,
                "=" => value == threshold,
                "<>" => value != threshold,
                _ => (bool?)null,
            };
        }

        return null;
    }

    private static string? Color(string section)
    {
        foreach (var tag in Tags(section))
        {
            var color = tag.ToUpperInvariant() switch
            {
                "BLACK" => "000000",
                "BLUE" => "0000FF",
                "CYAN" => "00FFFF",
                "GREEN" => "00FF00",
                "MAGENTA" => "FF00FF",
                "RED" => "FF0000",
                "WHITE" => "FFFFFF",
                "YELLOW" => "FFFF00",
                _ => null,
            };
            if (color != null)
            {
                return color;
            }
        }

        return null;
    }
}
