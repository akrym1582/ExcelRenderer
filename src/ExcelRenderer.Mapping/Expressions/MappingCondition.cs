using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ExcelRenderer.Mapping.Expressions;

/// <summary>Compiles mapping predicates with logical operators and parentheses.</summary>
internal sealed class MappingCondition
{
    private readonly string text;
    private readonly Func<object?, IReadOnlyDictionary<string, object?>, bool> evaluate;
    private readonly bool pageBreak;
    private int position;
    private int depth;

    /// <summary>Initializes a new instance of the <see cref="MappingCondition"/> class.</summary>
    /// <param name="text">The condition or complete page-break directive.</param>
    /// <param name="pageBreak">Whether to use the page-break comparison grammar.</param>
    internal MappingCondition(string text, bool pageBreak = false)
    {
        this.pageBreak = pageBreak;
        if (pageBreak)
        {
            var prefix = Regex.Match(text, @"^\*\*@page-break\s+if\s+");
            if (!prefix.Success)
            {
                throw new FormatException("Expected **@page-break if condition.");
            }

            text = text.Substring(prefix.Length);
        }

        this.text = text;
        evaluate = ParseOr();
        SkipWhitespace();
        if (position != this.text.Length)
        {
            throw new FormatException($"Unexpected text in {ConditionName}.");
        }
    }

    /// <summary>Gets all referenced paths, including those in short-circuited branches.</summary>
    internal List<DataPath> Paths { get; } = new List<DataPath>();

    private string ConditionName => pageBreak ? "page-break condition" : "if condition";

    /// <summary>Evaluates the condition with logical short-circuiting.</summary>
    /// <param name="root">The caller data.</param>
    /// <param name="aliases">The scoped aliases.</param>
    /// <returns>The condition result.</returns>
    internal bool Evaluate(object? root, IReadOnlyDictionary<string, object?> aliases) => evaluate(root, aliases);

    private bool Compare(object? value, decimal? divisor, string comparison, decimal expected)
    {
        value = DataPath.Scalar(value);
        if (value is not byte && value is not sbyte && value is not short && value is not ushort &&
            value is not int && value is not uint && value is not long && value is not ulong &&
            value is not float && value is not double && value is not decimal)
        {
            throw new InvalidOperationException($"A {ConditionName} requires a numeric alias.");
        }

        var number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
        if (divisor.HasValue)
        {
            number %= divisor.Value;
        }

        return comparison switch
        {
            "==" => number == expected,
            "!=" => number != expected,
            "<=" => number <= expected,
            ">=" => number >= expected,
            "<" => number < expected,
            ">" => number > expected,
            _ => throw new InvalidOperationException($"Unsupported comparison in {ConditionName}."),
        };
    }

    private Func<object?, IReadOnlyDictionary<string, object?>, bool> ParseOr()
    {
        var operands = new List<Func<object?, IReadOnlyDictionary<string, object?>, bool>> { ParseAnd() };
        while (Take("||"))
        {
            operands.Add(ParseAnd());
        }

        return (root, aliases) => operands.Exists(operand => operand(root, aliases));
    }

    private Func<object?, IReadOnlyDictionary<string, object?>, bool> ParseAnd()
    {
        var operands = new List<Func<object?, IReadOnlyDictionary<string, object?>, bool>> { ParseUnary() };
        while (Take("&&"))
        {
            operands.Add(ParseUnary());
        }

        return (root, aliases) => operands.TrueForAll(operand => operand(root, aliases));
    }

    private Func<object?, IReadOnlyDictionary<string, object?>, bool> ParseUnary()
    {
        if (++depth > 64)
        {
            throw new FormatException(pageBreak
                ? "Page-break conditions support at most 64 nested parentheses or negations."
                : "If conditions support at most 64 nested parentheses or negations.");
        }

        Func<object?, IReadOnlyDictionary<string, object?>, bool> result;
        if (Take("!"))
        {
            var operand = ParseUnary();
            result = (root, aliases) => !operand(root, aliases);
        }
        else if (Take("("))
        {
            result = ParseOr();
            if (!Take(")"))
            {
                throw new FormatException($"Expected ')' in {ConditionName}.");
            }
        }
        else
        {
            result = ParseComparison();
        }

        depth--;
        return result;
    }

    private Func<object?, IReadOnlyDictionary<string, object?>, bool> ParseComparison()
    {
        SkipWhitespace();
        var match = Regex.Match(text.Substring(position), @"^@?([\p{L}_][\p{L}\p{N}_]*)\s*(?:%\s*([0-9]+)\s*)?(==|!=|<=|>=|<|>)\s*(-?[0-9]+)");
        if (!match.Success)
        {
            if (pageBreak)
            {
                throw new FormatException("Expected alias [% positive-integer] comparison integer in page-break condition.");
            }

            return ParseBooleanPath();
        }

        position += match.Length;
        var path = new DataPath("@" + match.Groups[1].Value);
        Paths.Add(path);
        decimal? divisor = null;
        if (match.Groups[2].Success)
        {
            divisor = decimal.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            if (divisor == 0)
            {
                throw new FormatException(pageBreak
                    ? "A page-break remainder divisor must be positive."
                    : "An if condition remainder divisor must be positive.");
            }
        }

        var comparison = match.Groups[3].Value;
        var expected = decimal.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture);
        return (_, aliases) => Compare(path.Resolve(null, aliases), divisor, comparison, expected);
    }

    private Func<object?, IReadOnlyDictionary<string, object?>, bool> ParseBooleanPath()
    {
        var match = Regex.Match(text.Substring(position), @"^(?:@?[\p{L}_][\p{L}\p{N}_]*|\$)(?:\.[\p{L}_][\p{L}\p{N}_]*|\[(?:[0-9]+|'(?:[^'\\]|\\.)*'|""(?:[^""\\]|\\.)*"")\])*");
        if (!match.Success)
        {
            throw new FormatException("Expected a Boolean data path or numeric alias comparison in if condition.");
        }

        position += match.Length;
        var path = new DataPath(match.Value);
        Paths.Add(path);
        return (root, aliases) => DataPath.Scalar(path.Resolve(root, aliases, allowMissing: true)) switch
        {
            null => false,
            bool value => value,
            _ => throw new InvalidOperationException("An if condition requires a Boolean value."),
        };
    }

    private bool Take(string token)
    {
        SkipWhitespace();
        if (string.CompareOrdinal(text, position, token, 0, token.Length) != 0)
        {
            return false;
        }

        position += token.Length;
        return true;
    }

    private void SkipWhitespace()
    {
        while (position < text.Length && char.IsWhiteSpace(text[position]))
        {
            position++;
        }
    }
}
