using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ExcelRenderer.Mapping;

/// <summary>Separates data paths from explicit CLR formatting instructions.</summary>
internal sealed class CellExpression
{
    private readonly string? function;
    private readonly string[] arguments = System.Array.Empty<string>();

    /// <summary>Initializes a new instance of the <see cref="CellExpression"/> class.</summary>
    /// <param name="text">The text.</param>
    internal CellExpression(string text)
    {
        var quote = '\0';
        var escaped = false;
        var separator = -1;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (escaped)
            {
                escaped = false;
            }
            else if (c == '\\' && quote != '\0')
            {
                escaped = true;
            }
            else if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }
            }
            else if (c == '\'' || c == '"')
            {
                quote = c;
            }
            else if (c == '|')
            {
                separator = i;
                break;
            }
        }

        Path = new DataPath((separator < 0 ? text : text.Substring(0, separator)).Trim());
        if (separator >= 0)
        {
            var match = Regex.Match(text.Substring(separator + 1).Trim(), @"^(format|date)\((.*)\)$", RegexOptions.Singleline);
            if (!match.Success)
            {
                throw new FormatException("Expected format(\"format\"[, \"culture\"]) or date(\"format\"[, \"culture\"]).");
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
}
