using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ExcelRenderer.Mapping.Configuration;

namespace ExcelRenderer.Mapping.Expressions;

/// <summary>Separates data paths from explicit CLR formatting instructions.</summary>
internal sealed class CellExpression
{
    private readonly string? function;
    private readonly DataPath? colorPath;
    private readonly string[] arguments = System.Array.Empty<string>();

    /// <summary>Initializes a new instance of the <see cref="CellExpression"/> class.</summary>
    /// <param name="text">The text.</param>
    internal CellExpression(string text)
    {
        var segments = Split(text);
        Path = new DataPath(segments[0].Trim());
        foreach (var segment in segments.GetRange(1, segments.Count - 1))
        {
            var match = Regex.Match(segment.Trim(), @"^(format|date|font-color)\((.*)\)$", RegexOptions.Singleline);
            if (!match.Success)
            {
                throw new FormatException("Expected format(), date(), or font-color().");
            }

            if (match.Groups[1].Value == "font-color")
            {
                if (colorPath is not null)
                {
                    throw new FormatException("font-color may only appear once.");
                }

                colorPath = new DataPath(match.Groups[2].Value.Trim());
                continue;
            }

            if (function is not null)
            {
                throw new FormatException("Only one format() or date() conversion is supported.");
            }

            function = match.Groups[1].Value;
            arguments = JsonSerializer.Deserialize<string[]>("[" + match.Groups[2].Value + "]")!;
            if (arguments.Length < 1 || arguments.Length > 2 || System.Array.Exists(arguments, a => a is null))
            {
                throw new FormatException("Formatting requires one or two string arguments.");
            }

            if (arguments.Length == 2)
            {
                CultureInfo.GetCultureInfo(arguments[1]);
            }
        }
    }

    /// <summary>Gets the compiled data path.</summary>
    internal DataPath Path { get; }

    /// <summary>Gets paths used by cell decorations.</summary>
    internal IEnumerable<DataPath> DecorationPaths
    {
        get
        {
            if (colorPath is not null)
            {
                yield return colorPath;
            }
        }
    }

    /// <summary>Evaluates an optional font color.</summary>
    /// <param name="root">Root data.</param>
    /// <param name="aliases">Scoped aliases.</param>
    /// <param name="element">Repeated element.</param>
    /// <param name="substituteWildcard">Whether to substitute the wildcard.</param>
    /// <returns>A six-digit RGB color or null.</returns>
    internal string? EvaluateColor(object? root, IReadOnlyDictionary<string, object?> aliases, object? element, bool substituteWildcard)
    {
        if (colorPath is null)
        {
            return null;
        }

        var value = DataPath.Scalar(colorPath.Resolve(root, aliases, element, substituteWildcard));
        if (value is null)
        {
            return null;
        }

        if (value is not string text || !Regex.IsMatch(text, @"^#[0-9a-fA-F]{6}\z"))
        {
            throw new FormatException("font-color requires #RRGGBB or null.");
        }

        return text.Substring(1).ToUpperInvariant();
    }

    /// <summary>Resolves and formats a cell expression.</summary>
    /// <param name="root">The root.</param>
    /// <param name="aliases">The aliases.</param>
    /// <param name="options">The options.</param>
    /// <param name="element">The element.</param>
    /// <param name="substituteWildcard">The substitute wildcard.</param>
    /// <returns>The formatted or typed value.</returns>
    internal object? Evaluate(object? root, IReadOnlyDictionary<string, object?> aliases, MappingOptions options, object? element = null, bool substituteWildcard = false)
    {
        var value = DataPath.Scalar(Path.Resolve(root, aliases, element, substituteWildcard));
        if (value is null || function is null)
        {
            return value;
        }

        var culture = arguments.Length == 2 ? CultureInfo.GetCultureInfo(arguments[1]) : options.Culture;
        if (function == "date")
        {
            if (value is string text)
            {
                if (!Regex.IsMatch(text, @"^\d{4}-\d{2}-\d{2}(?:T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})?)?$"))
                {
                    throw new FormatException("date() requires an ISO 8601 date or timestamp.");
                }

                value = Regex.IsMatch(text, @"(?:Z|[+-]\d{2}:\d{2})$")
                    ? (object)DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.None)
                    : DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            }

            if (value is not DateTime && value is not DateTimeOffset)
            {
                throw new FormatException("date() requires a date or an ISO 8601 string.");
            }
        }

        if (value is not IFormattable formattable)
        {
            throw new FormatException("The mapped value does not support IFormattable formatting.");
        }

        return formattable.ToString(arguments[0], culture);
    }

    private static List<string> Split(string text)
    {
        var result = new List<string>();
        var quote = '\0';
        var escaped = false;
        var start = 0;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (escaped)
            {
                escaped = false;
            }
            else if (quote != '\0' && character == '\\')
            {
                escaped = true;
            }
            else if (quote != '\0')
            {
                if (character == quote)
                {
                    quote = '\0';
                }
            }
            else if (character is '\'' or '"')
            {
                quote = character;
            }
            else if (character == '|')
            {
                result.Add(text.Substring(start, index - start));
                start = index + 1;
            }
        }

        if (quote != '\0')
        {
            throw new FormatException("Unterminated expression quote.");
        }

        result.Add(text.Substring(start));
        return result;
    }
}
