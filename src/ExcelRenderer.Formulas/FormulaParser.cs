using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ExcelRenderer.Formulas;

/// <summary>Parses the supported invariant A1 formula language.</summary>
public static class FormulaParser
{
    /// <summary>Parses a formula, preserving its source positions.</summary>
    /// <param name="formula">Formula text, optionally beginning with equals.</param>
    /// <param name="options">Resource limits.</param>
    /// <returns>A tree or diagnostics.</returns>
    public static FormulaParseResult Parse(string formula, FormulaEvaluationOptions? options = null)
    {
        if (formula == null)
        {
            throw new ArgumentNullException(nameof(formula));
        }

        options ??= new FormulaEvaluationOptions();
        if (formula.Length > options.MaxFormulaLength)
        {
            return new FormulaParseResult(null, new FormulaDiagnostic("ResourceLimitExceeded", "Formula length limit exceeded."));
        }

        try
        {
            var parser = new Parser(formula, options);
            return new FormulaParseResult(new ParsedFormula(formula, parser.Parse()));
        }
        catch (ParseFailure failure)
        {
            return new FormulaParseResult(null, failure.Diagnostic);
        }
    }

    private sealed class ParseFailure : Exception
    {
        internal ParseFailure(FormulaDiagnostic diagnostic)
        {
            Diagnostic = diagnostic;
        }

        internal FormulaDiagnostic Diagnostic { get; }
    }

    private sealed class Parser
    {
        private readonly string source;
        private readonly FormulaEvaluationOptions options;
        private readonly Dictionary<FormulaNode, int> heights = new Dictionary<FormulaNode, int>();
        private int position;
        private int nodes;
        private int depth;
        private string token = string.Empty;
        private int start;
        private int end;
        private bool quoted;
        private bool literal;

        internal Parser(string source, FormulaEvaluationOptions options)
        {
            this.source = source;
            this.options = options;
            position = source.StartsWith("=", StringComparison.Ordinal) ? 1 : 0;
            Next();
        }

        internal FormulaNode Parse()
        {
            var result = Expression(0);
            if (token.Length != 0)
            {
                Fail("UnsupportedSyntax", "Unexpected trailing token or reference operator.");
            }

            return result;
        }

        private static bool IsR1C1Reference(string text)
        {
            if (text.Length < 4 || char.ToUpperInvariant(text[0]) != 'R')
            {
                return false;
            }

            int index = 1;
            while (index < text.Length && text[index] >= '0' && text[index] <= '9')
            {
                index++;
            }

            if (index == 1 || index >= text.Length || char.ToUpperInvariant(text[index++]) != 'C')
            {
                return false;
            }

            int columnStart = index;
            while (index < text.Length && text[index] >= '0' && text[index] <= '9')
            {
                index++;
            }

            return index > columnStart && index == text.Length;
        }

        private static bool IsIdentifier(string text)
        {
            return text.Length > 0 && (char.IsLetter(text[0]) || text[0] == '_' || text[0] == '\\' || text[0] == '$');
        }

        private static int Precedence(string op)
        {
            switch (op)
            {
                case "=": case "<>": case "<": case "<=": case ">": case ">=": return 0;
                case "&": return 1;
                case "+": case "-": return 2;
                case "*": case "/": return 3;
                case "^": return 6;
                default: return -1;
            }
        }

        private FormulaNode Expression(int minimum)
        {
            if (++depth > options.MaxSyntaxDepth)
            {
                Fail("ResourceLimitExceeded", "Syntax depth limit exceeded.");
            }

            var left = Primary();
            while (true)
            {
                if (token == "%" && minimum <= 7)
                {
                    var last = end;
                    Next();
                    left = Node(FormulaNodeKind.Percent, left.Span.Start, last, children: new[] { left });
                    continue;
                }

                int precedence = Precedence(token);
                if (precedence < minimum)
                {
                    break;
                }

                string op = token;
                Next();
                var right = Expression(precedence + 1);
                left = Node(FormulaNodeKind.Binary, left.Span.Start, right.Span.Start + right.Span.Length, op, children: new[] { left, right });
            }

            depth--;
            return left;
        }

        private FormulaNode Primary()
        {
            int first = start;
            if (token == "+" || token == "-")
            {
                string op = token;
                Next();
                var child = Expression(8);
                return Node(FormulaNodeKind.Unary, first, child.Span.Start + child.Span.Length, op, children: new[] { child });
            }

            if (token == "(")
            {
                Next();
                var child = Expression(0);
                Require(")");
                int last = end;
                Next();
                return Node(FormulaNodeKind.Parenthesized, first, last, children: new[] { child });
            }

            string text = token;
            bool wasQuoted = quoted;
            bool wasLiteral = literal;
            int tokenEnd = end;
            if (text.Length == 0 && !wasLiteral)
            {
                Fail("SyntaxError", "Expected an expression.");
            }

            Next();
            if (wasLiteral)
            {
                return Node(FormulaNodeKind.Text, first, tokenEnd, text);
            }

            if (token == "(" && !wasQuoted && IsIdentifier(text))
            {
                Next();
                var args = new List<FormulaNode>();
                if (token != ")")
                {
                    while (true)
                    {
                        args.Add(token == "," || token == ")" ? Node(FormulaNodeKind.Missing, start, start) : Expression(0));
                        if (token != ",")
                        {
                            break;
                        }

                        Next();
                    }
                }

                Require(")");
                int last = end;
                Next();
                return Node(FormulaNodeKind.Function, first, last, text, children: args);
            }

            string? sheet = null;
            if (token == "!")
            {
                sheet = text;
                Next();
                text = token;
                tokenEnd = end;
                if (quoted || literal || text.Length == 0)
                {
                    Fail("SyntaxError", "Expected a reference after sheet qualifier.");
                }

                Next();
            }
            else if (wasQuoted)
            {
                Fail("SyntaxError", "Quoted sheet name requires an exclamation mark.");
            }

            // An exact RnCn token is unambiguously R1C1 syntax. RC1 remains
            // a valid A1 address, and functions and sheet names were handled above.
            if (IsR1C1Reference(text))
            {
                throw new ParseFailure(new FormulaDiagnostic("UnsupportedSyntax", "R1C1 references are unsupported.", new SourceSpan(first, tokenEnd - first), feature: "R1C1"));
            }

            if (TryAddress(text, token == ":", out int row, out int column, out bool absoluteRow, out bool absoluteColumn))
            {
                var left = Node(FormulaNodeKind.Cell, first, tokenEnd, text, sheet: sheet, row: row, column: column, absoluteRow: absoluteRow, absoluteColumn: absoluteColumn);
                if (token != ":")
                {
                    return left;
                }

                Next();
                int rightStart = start;
                string rightText = token;
                int rightEnd = end;
                if (!TryAddress(rightText, true, out int rightRow, out int rightColumn, out bool rightAbsoluteRow, out bool rightAbsoluteColumn) || (row == 0) != (rightRow == 0) || (column == 0) != (rightColumn == 0))
                {
                    Fail("UnsupportedSyntax", "Range endpoints must have matching coordinate types on one sheet.");
                }

                Next();
                var right = Node(FormulaNodeKind.Cell, rightStart, rightEnd, rightText, sheet: sheet, row: rightRow, column: rightColumn, absoluteRow: rightAbsoluteRow, absoluteColumn: rightAbsoluteColumn);
                return Node(FormulaNodeKind.Range, first, rightEnd, children: new[] { left, right }, sheet: sheet);
            }

            if (text.IndexOf('$') >= 0)
            {
                Fail("InvalidReference", "Incomplete or invalid absolute reference.");
            }

            if (sheet != null)
            {
                if (!IsIdentifier(text))
                {
                    Fail("InvalidReference", "A sheet qualifier requires a cell, range or name.");
                }

                return Node(FormulaNodeKind.Name, first, tokenEnd, text, sheet: sheet);
            }

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
            {
                if (double.IsInfinity(number) || double.IsNaN(number))
                {
                    Fail("SyntaxError", "Numeric literal must be finite.");
                }

                return Node(FormulaNodeKind.Number, first, tokenEnd, text, number: number);
            }

            if (string.Equals(text, "TRUE", StringComparison.OrdinalIgnoreCase) || string.Equals(text, "FALSE", StringComparison.OrdinalIgnoreCase))
            {
                return Node(FormulaNodeKind.Boolean, first, tokenEnd, text, boolean: string.Equals(text, "TRUE", StringComparison.OrdinalIgnoreCase));
            }

            if (text.StartsWith("#", StringComparison.Ordinal))
            {
                return Node(FormulaNodeKind.Error, first, tokenEnd, text);
            }

            if (!IsIdentifier(text))
            {
                Fail("UnsupportedSyntax", "Unsupported expression syntax.");
            }

            return Node(FormulaNodeKind.Name, first, tokenEnd, text);
        }

        private FormulaNode Node(FormulaNodeKind kind, int first, int last, string text = "", double number = 0, bool boolean = false, IEnumerable<FormulaNode>? children = null, string? sheet = null, int row = 0, int column = 0, bool absoluteRow = false, bool absoluteColumn = false)
        {
            if (++nodes > options.MaxAstNodes)
            {
                Fail("ResourceLimitExceeded", "AST node limit exceeded.");
            }

            var childArray = children?.ToArray() ?? Array.Empty<FormulaNode>();
            int height = childArray.Length == 0 ? 1 : childArray.Max(child => heights[child]) + 1;
            if (height > options.MaxSyntaxDepth)
            {
                Fail("ResourceLimitExceeded", "AST depth limit exceeded.");
            }

            var result = new FormulaNode(kind, new SourceSpan(first, last - first), text, number, boolean, childArray, sheet, row, column, absoluteRow, absoluteColumn);
            heights.Add(result, height);
            return result;
        }

        private bool TryAddress(string text, bool partial, out int row, out int column, out bool absoluteRow, out bool absoluteColumn)
        {
            row = 0;
            column = 0;
            absoluteRow = false;
            absoluteColumn = false;
            int index = 0;
            bool initialAbsolute = text.StartsWith("$", StringComparison.Ordinal);
            if (initialAbsolute)
            {
                index++;
            }

            int letters = index;
            while (index < text.Length && ((text[index] >= 'A' && text[index] <= 'Z') || (text[index] >= 'a' && text[index] <= 'z')))
            {
                if (column < 100000)
                {
                    column = (column * 26) + char.ToUpperInvariant(text[index]) - 'A' + 1;
                }

                index++;
            }

            bool hasColumn = index > letters;
            absoluteColumn = hasColumn && initialAbsolute;
            absoluteRow = !hasColumn && initialAbsolute;
            if (hasColumn && index < text.Length && text[index] == '$')
            {
                absoluteRow = true;
                index++;
            }

            int digits = index;
            while (index < text.Length && text[index] >= '0' && text[index] <= '9')
            {
                if (row < 10000000)
                {
                    row = (row * 10) + text[index] - '0';
                }

                index++;
            }

            bool hasRow = index > digits;
            if (index != text.Length || (!hasColumn && !hasRow) || (!partial && (!hasColumn || !hasRow)))
            {
                return false;
            }

            if (column > 16384 || row > 1048576 || (hasRow && row == 0))
            {
                Fail("InvalidReference", "Reference exceeds the worksheet coordinate limits.");
            }

            return true;
        }

        private void Next()
        {
            while (position < source.Length && char.IsWhiteSpace(source[position]))
            {
                position++;
            }

            start = position;
            quoted = false;
            literal = false;
            if (position == source.Length)
            {
                token = string.Empty;
                end = position;
                return;
            }

            char current = source[position++];
            if (current == '"' || current == '\'')
            {
                quoted = current == '\'';
                literal = !quoted;
                var builder = new System.Text.StringBuilder();
                bool closed = false;
                while (position < source.Length)
                {
                    char value = source[position++];
                    if (value == current)
                    {
                        if (position < source.Length && source[position] == current)
                        {
                            position++;
                        }
                        else
                        {
                            closed = true;
                            break;
                        }
                    }

                    builder.Append(value);
                }

                if (!closed)
                {
                    Fail("SyntaxError", "Unterminated quoted text.");
                }

                token = builder.ToString();
            }
            else if (current == '#')
            {
                string[] errors = { "#NULL!", "#DIV/0!", "#VALUE!", "#REF!", "#NAME?", "#NUM!", "#N/A" };
                token = "#";
                foreach (string error in errors)
                {
                    if (source.Length - start >= error.Length && string.Compare(source, start, error, 0, error.Length, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        token = error;
                        position = start + error.Length;
                        break;
                    }
                }

                if (token == "#")
                {
                    Fail("UnsupportedSyntax", "Unsupported error or spill operator.");
                }
            }
            else if (char.IsDigit(current) || (current == '.' && position < source.Length && char.IsDigit(source[position])))
            {
                while (position < source.Length && (char.IsDigit(source[position]) || source[position] == '.'))
                {
                    position++;
                }

                if (position < source.Length && (source[position] == 'e' || source[position] == 'E'))
                {
                    position++;
                    if (position < source.Length && (source[position] == '+' || source[position] == '-'))
                    {
                        position++;
                    }

                    while (position < source.Length && char.IsDigit(source[position]))
                    {
                        position++;
                    }
                }

                token = source.Substring(start, position - start);
            }
            else if (char.IsLetter(current) || current == '_' || current == '\\' || current == '$')
            {
                while (position < source.Length && (char.IsLetterOrDigit(source[position]) || source[position] == '_' || source[position] == '.' || source[position] == '$' || source[position] == '\\'))
                {
                    position++;
                }

                token = source.Substring(start, position - start);
            }
            else
            {
                token = current.ToString();
                if ((current == '<' || current == '>') && position < source.Length && (source[position] == '=' || (current == '<' && source[position] == '>')))
                {
                    token += source[position++];
                }

                if ("+-*/^%&=<>():,!".IndexOf(current) < 0)
                {
                    Fail("UnsupportedSyntax", "Unsupported syntax: " + current);
                }
            }

            end = position;
        }

        private void Require(string expected)
        {
            if (token != expected)
            {
                Fail("SyntaxError", "Expected " + expected + ".");
            }
        }

        private void Fail(string code, string message)
        {
            throw new ParseFailure(new FormulaDiagnostic(code, message, new SourceSpan(start, Math.Max(1, position - start)), feature: token));
        }
    }
}
