using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace ExcelRenderer.Mapping;

/// <summary>Parses the supported JSONPath subset without losing CLR value types.</summary>
internal sealed class DataPath
{
    private readonly List<object> tokens = new List<object>();

    /// <summary>Initializes a new instance of the <see cref="DataPath"/> class.</summary>
    /// <param name="text">The text.</param>
    internal DataPath(string text)
    {
        Text = text;
        var position = 0;
        if (text.StartsWith("@", StringComparison.Ordinal))
        {
            position = 1;
            Alias = ReadName(text, ref position);
        }
        else if (text.StartsWith("$", StringComparison.Ordinal))
        {
            position = 1;
        }

        while (position < text.Length)
        {
            if (text[position] == '.')
            {
                position++;
                tokens.Add(ReadName(text, ref position));
            }
            else if (text[position] == '[')
            {
                position++;
                if (position < text.Length && (text[position] == '\'' || text[position] == '"'))
                {
                    var quote = text[position++];
                    var start = position;
                    var value = string.Empty;
                    while (position < text.Length && text[position] != quote)
                    {
                        if (text[position] == '\\')
                        {
                            value += text.Substring(start, position - start);
                            position++;
                            if (position >= text.Length || (text[position] != quote && text[position] != '\\'))
                            {
                                throw new FormatException("Only escaped quotes and backslashes are supported in bracket keys.");
                            }

                            value += text[position++];
                            start = position;
                        }
                        else
                        {
                            position++;
                        }
                    }

                    value += text.Substring(start, position - start);
                    Require(text, ref position, quote);
                    tokens.Add(value);
                }
                else if (position < text.Length && text[position] == '*')
                {
                    position++;
                    if (WildcardIndex >= 0)
                    {
                        throw new FormatException("Use nested array markers for multiple wildcards.");
                    }

                    WildcardIndex = tokens.Count;
                    tokens.Add('*');
                }
                else
                {
                    var start = position;
                    while (position < text.Length && char.IsDigit(text[position]))
                    {
                        position++;
                    }

                    if (!int.TryParse(text.Substring(start, position - start), NumberStyles.None, CultureInfo.InvariantCulture, out var index))
                    {
                        throw new FormatException("Expected a nonnegative array index.");
                    }

                    tokens.Add(index);
                }

                Require(text, ref position, ']');
            }
            else if (position == 0)
            {
                tokens.Add(ReadName(text, ref position));
            }
            else
            {
                throw new FormatException("Unsupported JSONPath syntax.");
            }
        }

        if (text.Length == 0)
        {
            throw new FormatException("A data path is required.");
        }
    }

    /// <summary>Gets the original expression text.</summary>
    internal string Text { get; }

    /// <summary>Gets the array element alias.</summary>
    internal string? Alias { get; }

    /// <summary>Gets the wildcard token index, or -1.</summary>
    internal int WildcardIndex { get; } = -1;

    /// <summary>Gets the normalized array prefix.</summary>
    internal string ArrayKey => (Alias ?? "$") + JsonSerializer.Serialize(tokens.Take(WildcardIndex).ToArray());

    /// <summary>Gets a value indicating whether the path ends in a wildcard.</summary>
    internal bool EndsInWildcard => WildcardIndex == tokens.Count - 1 && WildcardIndex >= 0;

    /// <summary>Converts JSON scalar values while retaining CLR types.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The scalar value.</returns>
    internal static object? Scalar(object? value)
    {
        if (value is not JsonElement json)
        {
            return value;
        }

        return json.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => json.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => json.TryGetDecimal(out var number) ? number : json.GetDouble(),
            _ => throw new InvalidOperationException("A mapped cell must resolve to a scalar value."),
        };
    }

    /// <summary>Resolves a path using root data and scoped aliases.</summary>
    /// <param name="root">The root.</param>
    /// <param name="aliases">The aliases.</param>
    /// <param name="element">The element.</param>
    /// <param name="substituteWildcard">The substitute wildcard.</param>
    /// <returns>The resolved value.</returns>
    internal object? Resolve(object? root, IReadOnlyDictionary<string, object?> aliases, object? element = null, bool substituteWildcard = false)
    {
        return ResolveCount(root, aliases, tokens.Count, element, substituteWildcard);
    }

    /// <summary>Resolves the array prefix with a bounded enumeration.</summary>
    /// <param name="root">The root.</param>
    /// <param name="aliases">The aliases.</param>
    /// <param name="limit">The limit.</param>
    /// <returns>The array elements.</returns>
    internal IReadOnlyList<object?> Array(object? root, IReadOnlyDictionary<string, object?> aliases, int limit)
    {
        if (WildcardIndex < 0)
        {
            throw new FormatException("An array path must contain [*].");
        }

        var value = ResolveCount(root, aliases, WildcardIndex, null, false);
        if (value is JsonElement json)
        {
            if (json.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("The array path does not resolve to an array.");
            }

            if (json.GetArrayLength() > limit)
            {
                throw new InvalidOperationException("The array exceeds MaxOutputRows.");
            }

            return json.EnumerateArray().Select(item => (object?)item).ToArray();
        }

        if (value is not IEnumerable enumerable || value is string || value is IDictionary)
        {
            throw new InvalidOperationException("The array path does not resolve to an array.");
        }

        var result = new List<object?>();
        foreach (var item in enumerable)
        {
            if (result.Count >= limit)
            {
                throw new InvalidOperationException("The array exceeds MaxOutputRows.");
            }

            result.Add(item);
        }

        return result;
    }

    private static string ReadName(string text, ref int position)
    {
        var start = position;
        if (position >= text.Length || (!char.IsLetter(text[position]) && text[position] != '_'))
        {
            throw new FormatException("Expected a property or alias name.");
        }

        position++;
        while (position < text.Length && (char.IsLetterOrDigit(text[position]) || text[position] == '_'))
        {
            position++;
        }

        return text.Substring(start, position - start);
    }

    private static void Require(string text, ref int position, char expected)
    {
        if (position >= text.Length || text[position++] != expected)
        {
            throw new FormatException($"Expected '{expected}'.");
        }
    }

    private static object? Member(object? value, string name)
    {
        if (value is JsonElement json)
        {
            if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty(name, out var member))
            {
                return member;
            }

            throw new InvalidOperationException($"Property '{name}' was not found (names are case-sensitive).");
        }

        if (value is IDictionary dictionary)
        {
            if (dictionary.Keys.Cast<object>().Any(key => key is string text && string.Equals(text, name, StringComparison.Ordinal)))
            {
                return dictionary[name];
            }

            throw new InvalidOperationException($"Property '{name}' was not found (names are case-sensitive).");
        }

        if (value is IDictionary<string, object?> objectDictionary)
        {
            if (objectDictionary.Keys.Contains(name, StringComparer.Ordinal) && objectDictionary.TryGetValue(name, out var entry))
            {
                return entry;
            }

            throw new InvalidOperationException($"Property '{name}' was not found (names are case-sensitive).");
        }

        var type = value?.GetType();
        var readOnlyDictionary = type?.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>) && i.GetGenericArguments()[0] == typeof(string));
        if (readOnlyDictionary is not null)
        {
            if (((IEnumerable<string>)readOnlyDictionary.GetProperty("Keys")!.GetValue(value)!).Contains(name, StringComparer.Ordinal))
            {
                return readOnlyDictionary.GetProperty("Item")!.GetValue(value, new object[] { name });
            }

            throw new InvalidOperationException($"Property '{name}' was not found (names are case-sensitive).");
        }

        var property = type?.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
        if (property?.GetMethod?.IsPublic == true && property.GetIndexParameters().Length == 0)
        {
            return property.GetValue(value);
        }

        throw new InvalidOperationException($"Property '{name}' was not found (names are case-sensitive).");
    }

    private object? ResolveCount(object? root, IReadOnlyDictionary<string, object?> aliases, int count, object? element, bool substituteWildcard)
    {
        var value = root;
        if (Alias is not null && !aliases.TryGetValue(Alias, out value))
        {
            throw new InvalidOperationException($"Array alias '{Alias}' is not in scope.");
        }

        for (var i = 0; i < count; i++)
        {
            if (tokens[i] is string name)
            {
                value = Member(value, name);
            }
            else if (tokens[i] is int index)
            {
                if (value is JsonElement json && json.ValueKind == JsonValueKind.Array && index < json.GetArrayLength())
                {
                    value = json[index];
                }
                else if (value is IList list && index < list.Count)
                {
                    value = list[index];
                }
                else
                {
                    throw new InvalidOperationException($"Array index {index} was not found.");
                }
            }
            else if (substituteWildcard)
            {
                value = element;
            }
            else
            {
                throw new InvalidOperationException("An unbound wildcard cannot resolve to a cell value.");
            }
        }

        return value;
    }
}
