using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ExcelRenderer.Formulas.Diagnostics;
using ExcelRenderer.Formulas.Evaluation;

namespace ExcelRenderer.Formulas.Syntax;

/// <summary>Parses the supported invariant A1 formula language.</summary>
public static class FormulaParser
{
    private enum TokenKind
    {
        End,
        Number,
        Identifier,
        StringLiteral,
        QuotedSheetName,
        ErrorLiteral,
        Operator,
        LeftParen,
        RightParen,
        Comma,
        Colon,
        Bang,
    }

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

    private readonly struct Token
    {
        internal Token(TokenKind kind, string value, SourceSpan span)
        {
            Kind = kind;
            Value = value;
            Span = span;
        }

        internal TokenKind Kind { get; }

        internal string Value { get; }

        internal SourceSpan Span { get; }
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
        private Token token;
        private int start;
        private int end;

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
            if (token.Kind != TokenKind.End)
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
                if (token.Kind == TokenKind.Operator && token.Value == "%" && minimum <= 7)
                {
                    var last = end;
                    Next();
                    left = Node(FormulaNodeKind.Percent, left.Span.Start, last, children: new[] { left });
                    continue;
                }

                int precedence = token.Kind == TokenKind.Operator ? Precedence(token.Value) : -1;
                if (precedence < minimum)
                {
                    break;
                }

                string op = token.Value;
                Next();
                var right = Expression(precedence + 1);
                left = Node(FormulaNodeKind.Binary, left.Span.Start, right.Span.Start + right.Span.Length, op, children: new[] { left, right });
            }

            depth--;
            return left;
        }

        private FormulaNode Primary()
        {
            int first = token.Span.Start;
            if (token.Kind == TokenKind.Operator && (token.Value == "+" || token.Value == "-"))
            {
                string op = token.Value;
                Next();
                var child = Expression(8);
                return Node(FormulaNodeKind.Unary, first, child.Span.Start + child.Span.Length, op, children: new[] { child });
            }

            if (token.Kind == TokenKind.LeftParen)
            {
                Next();
                var child = Expression(0);
                Require(TokenKind.RightParen);
                int last = end;
                Next();
                return Node(FormulaNodeKind.Parenthesized, first, last, children: new[] { child });
            }

            string text = token.Value;
            var kind = token.Kind;
            int tokenEnd = token.Span.Start + token.Span.Length;
            if (kind == TokenKind.End || (kind == TokenKind.QuotedSheetName && text.Length == 0))
            {
                Fail("SyntaxError", "Expected an expression.");
            }

            Next();
            if (kind == TokenKind.StringLiteral)
            {
                return Node(FormulaNodeKind.Text, first, tokenEnd, text);
            }

            if (token.Kind == TokenKind.LeftParen && kind == TokenKind.Identifier)
            {
                Next();
                var args = new List<FormulaNode>();
                if (token.Kind != TokenKind.RightParen)
                {
                    while (true)
                    {
                        args.Add(token.Kind == TokenKind.Comma || token.Kind == TokenKind.RightParen ? Node(FormulaNodeKind.Missing, start, start) : Expression(0));
                        if (token.Kind != TokenKind.Comma)
                        {
                            break;
                        }

                        Next();
                    }
                }

                Require(TokenKind.RightParen);
                int last = end;
                Next();
                return Node(FormulaNodeKind.Function, first, last, text, children: args);
            }

            string? sheet = null;
            if (token.Kind == TokenKind.Bang && (kind == TokenKind.Identifier || kind == TokenKind.Number || kind == TokenKind.QuotedSheetName))
            {
                sheet = text;
                Next();
                text = token.Value;
                kind = token.Kind;
                tokenEnd = end;
                if (kind != TokenKind.Identifier && kind != TokenKind.Number)
                {
                    Fail("SyntaxError", "Expected a reference after sheet qualifier.");
                }

                Next();
            }
            else if (kind == TokenKind.QuotedSheetName)
            {
                Fail("SyntaxError", "Quoted sheet name requires an exclamation mark.");
            }

            // An exact RnCn token is unambiguously R1C1 syntax. RC1 remains
            // a valid A1 address, and functions and sheet names were handled above.
            if (kind == TokenKind.Identifier && IsR1C1Reference(text))
            {
                throw new ParseFailure(new FormulaDiagnostic("UnsupportedSyntax", "R1C1 references are unsupported.", new SourceSpan(first, tokenEnd - first), feature: "R1C1"));
            }

            if ((kind == TokenKind.Identifier || kind == TokenKind.Number) && TryAddress(text, token.Kind == TokenKind.Colon, out int row, out int column, out bool absoluteRow, out bool absoluteColumn))
            {
                var left = Node(FormulaNodeKind.Cell, first, tokenEnd, text, sheet: sheet, row: row, column: column, absoluteRow: absoluteRow, absoluteColumn: absoluteColumn);
                if (token.Kind != TokenKind.Colon)
                {
                    return left;
                }

                Next();
                int rightStart = start;
                string rightText = token.Value;
                int rightEnd = end;
                if (token.Kind != TokenKind.Identifier && token.Kind != TokenKind.Number)
                {
                    Fail("UnsupportedSyntax", "Expected a range endpoint reference.");
                }

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
                if (kind != TokenKind.Identifier || !IsIdentifier(text))
                {
                    Fail("InvalidReference", "A sheet qualifier requires a cell, range or name.");
                }

                return Node(FormulaNodeKind.Name, first, tokenEnd, text, sheet: sheet);
            }

            if (kind == TokenKind.Number && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
            {
                if (double.IsInfinity(number) || double.IsNaN(number))
                {
                    Fail("SyntaxError", "Numeric literal must be finite.");
                }

                return Node(FormulaNodeKind.Number, first, tokenEnd, text, number: number);
            }

            if (kind == TokenKind.Identifier && (string.Equals(text, "TRUE", StringComparison.OrdinalIgnoreCase) || string.Equals(text, "FALSE", StringComparison.OrdinalIgnoreCase)))
            {
                return Node(FormulaNodeKind.Boolean, first, tokenEnd, text, boolean: string.Equals(text, "TRUE", StringComparison.OrdinalIgnoreCase));
            }

            if (kind == TokenKind.ErrorLiteral)
            {
                return Node(FormulaNodeKind.Error, first, tokenEnd, text);
            }

            if (kind != TokenKind.Identifier || !IsIdentifier(text))
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
            var kind = TokenKind.End;
            string valueText;
            if (position == source.Length)
            {
                token = new Token(TokenKind.End, string.Empty, new SourceSpan(start, 0));
                end = position;
                return;
            }

            char current = source[position++];
            if (current == '"' || current == '\'')
            {
                kind = current == '\'' ? TokenKind.QuotedSheetName : TokenKind.StringLiteral;
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

                valueText = builder.ToString();
            }
            else if (current == '#')
            {
                kind = TokenKind.ErrorLiteral;
                string[] errors = { "#NULL!", "#DIV/0!", "#VALUE!", "#REF!", "#NAME?", "#NUM!", "#N/A" };
                valueText = "#";
                foreach (string error in errors)
                {
                    if (source.Length - start >= error.Length && string.Compare(source, start, error, 0, error.Length, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        valueText = error;
                        position = start + error.Length;
                        break;
                    }
                }

                if (valueText == "#")
                {
                    Fail("UnsupportedSyntax", "Unsupported error or spill operator.");
                }
            }
            else if (char.IsDigit(current) || (current == '.' && position < source.Length && char.IsDigit(source[position])))
            {
                kind = TokenKind.Number;
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

                valueText = source.Substring(start, position - start);
            }
            else if (char.IsLetter(current) || current == '_' || current == '\\' || current == '$')
            {
                kind = TokenKind.Identifier;
                while (position < source.Length && (char.IsLetterOrDigit(source[position]) || source[position] == '_' || source[position] == '.' || source[position] == '$' || source[position] == '\\'))
                {
                    position++;
                }

                valueText = source.Substring(start, position - start);
            }
            else
            {
                kind = current == '(' ? TokenKind.LeftParen : current == ')' ? TokenKind.RightParen : current == ',' ? TokenKind.Comma : current == ':' ? TokenKind.Colon : current == '!' ? TokenKind.Bang : TokenKind.Operator;
                valueText = current.ToString();
                if ((current == '<' || current == '>') && position < source.Length && (source[position] == '=' || (current == '<' && source[position] == '>')))
                {
                    valueText += source[position++];
                }

                if ("+-*/^%&=<>():,!".IndexOf(current) < 0)
                {
                    Fail("UnsupportedSyntax", "Unsupported syntax: " + current);
                }
            }

            end = position;
            token = new Token(kind, valueText, new SourceSpan(start, end - start));
        }

        private void Require(TokenKind expected)
        {
            if (token.Kind != expected)
            {
                Fail("SyntaxError", "Expected " + expected + ".");
            }
        }

        private void Fail(string code, string message)
        {
            throw new ParseFailure(new FormulaDiagnostic(code, message, new SourceSpan(start, Math.Max(1, position - start)), feature: token.Value));
        }
    }
}
