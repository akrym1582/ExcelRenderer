using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ExcelRenderer.Mapping.Expressions;

/// <summary>Compiles numeric comparisons combined with logical operators and parentheses.</summary>
internal sealed class PageBreakCondition
{
    private readonly string text;
    private readonly Func<IReadOnlyDictionary<string, object?>, bool> evaluate;
    private int position;
    private int depth;

    /// <summary>Initializes a new instance of the <see cref="PageBreakCondition"/> class.</summary>
    /// <param name="text">The complete page-break directive.</param>
    internal PageBreakCondition(string text)
    {
        var prefix = Regex.Match(text, @"^\*\*@page-break\s+if\s+");
        if (!prefix.Success)
        {
            throw new FormatException("Expected **@page-break if condition.");
        }

        this.text = text.Substring(prefix.Length);
        evaluate = ParseOr();
        SkipWhitespace();
        if (position != this.text.Length)
        {
            throw new FormatException("Unexpected text in page-break condition.");
        }
    }

    /// <summary>Gets all alias paths, including those in short-circuited branches.</summary>
    internal List<DataPath> Paths { get; } = new List<DataPath>();

    /// <summary>Evaluates the condition for one expanded row with logical short-circuiting.</summary>
    /// <param name="aliases">The scoped aliases.</param>
    /// <returns>Whether this row requests a page break.</returns>
    internal bool Evaluate(IReadOnlyDictionary<string, object?> aliases) => evaluate(aliases);

    private static bool Compare(object? value, decimal? divisor, string comparison, decimal expected)
    {
        value = DataPath.Scalar(value);
        if (value is not byte && value is not sbyte && value is not short && value is not ushort &&
            value is not int && value is not uint && value is not long && value is not ulong &&
            value is not float && value is not double && value is not decimal)
        {
            throw new InvalidOperationException("A page-break condition requires a numeric alias.");
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
            _ => throw new InvalidOperationException("Unsupported page-break comparison."),
        };
    }

    private Func<IReadOnlyDictionary<string, object?>, bool> ParseOr()
    {
        var operands = new List<Func<IReadOnlyDictionary<string, object?>, bool>> { ParseAnd() };
        while (Take("||"))
        {
            operands.Add(ParseAnd());
        }

        return aliases => operands.Exists(operand => operand(aliases));
    }

    private Func<IReadOnlyDictionary<string, object?>, bool> ParseAnd()
    {
        var operands = new List<Func<IReadOnlyDictionary<string, object?>, bool>> { ParseUnary() };
        while (Take("&&"))
        {
            operands.Add(ParseUnary());
        }

        return aliases => operands.TrueForAll(operand => operand(aliases));
    }

    private Func<IReadOnlyDictionary<string, object?>, bool> ParseUnary()
    {
        if (++depth > 64)
        {
            throw new FormatException("Page-break conditions support at most 64 nested parentheses or negations.");
        }

        Func<IReadOnlyDictionary<string, object?>, bool> result;
        if (Take("!"))
        {
            var operand = ParseUnary();
            result = aliases => !operand(aliases);
        }
        else if (Take("("))
        {
            result = ParseOr();
            if (!Take(")"))
            {
                throw new FormatException("Expected ')' in page-break condition.");
            }
        }
        else
        {
            result = ParseComparison();
        }

        depth--;
        return result;
    }

    private Func<IReadOnlyDictionary<string, object?>, bool> ParseComparison()
    {
        SkipWhitespace();
        var match = Regex.Match(text.Substring(position), @"^@?([\p{L}_][\p{L}\p{N}_]*)\s*(?:%\s*([0-9]+)\s*)?(==|!=|<=|>=|<|>)\s*(-?[0-9]+)");
        if (!match.Success)
        {
            throw new FormatException("Expected alias [% positive-integer] comparison integer in page-break condition.");
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
                throw new FormatException("A page-break remainder divisor must be positive.");
            }
        }

        var comparison = match.Groups[3].Value;
        var expected = decimal.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture);
        return aliases => Compare(path.Resolve(null, aliases), divisor, comparison, expected);
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
