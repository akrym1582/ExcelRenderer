using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using ExcelRenderer.OpenXml.Writing.Plans;

namespace ExcelRenderer.Mapping.Expressions;

/// <summary>Compiles and evaluates an image directive without modifying a workbook.</summary>
internal sealed class ImageExpression
{
    private readonly Dictionary<string, string> arguments = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DataPath> paths = new(StringComparer.Ordinal);

    /// <summary>Initializes a new instance of the <see cref="ImageExpression"/> class.</summary>
    /// <param name="text">Directive arguments following the image keyword.</param>
    internal ImageExpression(string text)
    {
        var tokens = Tokens(text);
        if (tokens.Count == 0)
        {
            throw new FormatException("Expected **@image path [name=value ...].");
        }

        Path = new DataPath(tokens[0]);
        foreach (var token in tokens.Skip(1))
        {
            var separator = token.IndexOf('=');
            if (separator <= 0 || separator == token.Length - 1)
            {
                throw new FormatException("Image arguments require name=value.");
            }

            var name = token.Substring(0, separator);
            var value = token.Substring(separator + 1);
            if (name is not "width" and not "height" and not "scale" and not "offset-x" and not "offset-y" and not "fit" || arguments.ContainsKey(name))
            {
                throw new FormatException("Unknown or duplicate image argument: " + name);
            }

            arguments.Add(name, value);
            if (value.StartsWith("$", StringComparison.Ordinal) || value.StartsWith("@", StringComparison.Ordinal))
            {
                paths.Add(name, new DataPath(value));
            }
            else if (name == "fit")
            {
                ValidateFit(value);
            }
            else
            {
                Number(value, name);
            }
        }

        if (arguments.ContainsKey("scale") && (arguments.ContainsKey("width") || arguments.ContainsKey("height")))
        {
            throw new FormatException("scale cannot be combined with width or height.");
        }

        if (arguments.ContainsKey("fit") && (!arguments.ContainsKey("width") || !arguments.ContainsKey("height")))
        {
            throw new FormatException("fit requires both width and height.");
        }
    }

    /// <summary>Gets the image data path.</summary>
    internal DataPath Path { get; }

    /// <summary>Gets every path requiring scope validation.</summary>
    internal IEnumerable<DataPath> Paths => new[] { Path }.Concat(paths.Values);

    /// <summary>Evaluates an image and its placement.</summary>
    /// <param name="root">Root input.</param>
    /// <param name="aliases">Scoped aliases.</param>
    /// <param name="element">Repeated element.</param>
    /// <param name="wildcard">Whether to substitute wildcard paths.</param>
    /// <returns>The image or null.</returns>
    internal CellImageEdit? Evaluate(object? root, IReadOnlyDictionary<string, object?> aliases, object? element, bool wildcard)
    {
        object? Argument(string name) => paths.TryGetValue(name, out var path) ? DataPath.Scalar(path.Resolve(root, aliases, element, wildcard)) : arguments[name];
        double? Dimension(string name) => arguments.ContainsKey(name) ? Number(Argument(name), name) : null;
        var width = Dimension("width");
        var height = Dimension("height");
        var scale = Dimension("scale") ?? 1;
        var offsetX = Dimension("offset-x") ?? 0;
        var offsetY = Dimension("offset-y") ?? 0;
        var fit = arguments.ContainsKey("fit") ? Argument("fit") as string : "stretch";
        ValidateFit(fit);
        var value = Path.Resolve(root, aliases, element, wildcard);
        if (value is JsonElement json)
        {
            value = DataPath.Scalar(json);
        }

        if (value is null)
        {
            return null;
        }

        var bytes = value switch
        {
            byte[] binary => binary,
            string encoded => Convert.FromBase64String(encoded),
            _ => throw new FormatException("Images require byte[] or a Base64 string."),
        };
        return new CellImageEdit(bytes, width, height, scale, offsetX, offsetY, fit == "contain");
    }

    private static void ValidateFit(string? value)
    {
        if (value is not "contain" and not "stretch")
        {
            throw new FormatException("fit must be contain or stretch.");
        }
    }

    private static double Number(object? value, string name)
    {
        if (value is null || value is bool || value is char || value is DateTime || value is not IConvertible)
        {
            throw new FormatException("Image argument " + name + " requires a number or a dimension string.");
        }

        var text = Convert.ToString(value, CultureInfo.InvariantCulture)!;
        var factor = name == "scale" ? 1.0 : 0.75;
        if (name != "scale" && (text.EndsWith("px", StringComparison.Ordinal) || text.EndsWith("pt", StringComparison.Ordinal)))
        {
            factor = text.EndsWith("pt", StringComparison.Ordinal) ? 1 : 0.75;
            text = text.Substring(0, text.Length - 2);
        }

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || double.IsNaN(number) || double.IsInfinity(number) ||
            (name is "width" or "height" or "scale" && number <= 0) || Math.Abs(number * factor) >= long.MaxValue / 12700.0)
        {
            throw new FormatException("Invalid image argument: " + name);
        }

        return number * factor;
    }

    private static List<string> Tokens(string text)
    {
        var result = new List<string>();
        var token = new StringBuilder();
        var quote = '\0';
        var escaped = false;
        foreach (var character in text)
        {
            if (quote == '\0' && char.IsWhiteSpace(character))
            {
                if (token.Length > 0)
                {
                    result.Add(token.ToString());
                    token.Clear();
                }

                continue;
            }

            token.Append(character);
            if (escaped)
            {
                escaped = false;
            }
            else if (quote != '\0' && character == '\\')
            {
                escaped = true;
            }
            else if (quote != '\0' && character == quote)
            {
                quote = '\0';
            }
            else if (quote == '\0' && character is '\'' or '"')
            {
                quote = character;
            }
        }

        if (quote != '\0')
        {
            throw new FormatException("Unterminated image argument quote.");
        }

        if (token.Length > 0)
        {
            result.Add(token.ToString());
        }

        return result;
    }
}
