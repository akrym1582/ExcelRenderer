using System;
using System.Globalization;
using System.Linq;
using ExcelRenderer.Formulas.Syntax;

namespace ExcelRenderer.Formulas.References;

/// <summary>Safely serializes and transforms parsed references without modifying string literals.</summary>
public static class FormulaRewriter
{
    /// <summary>Copies a parsed formula without parsing or serializing it again.</summary>
    /// <param name="formula">Original parsed formula.</param>
    /// <param name="rowDelta">Relative row displacement.</param>
    /// <param name="columnDelta">Relative column displacement.</param>
    /// <returns>An immutable syntax tree with moved relative references.</returns>
    public static ParsedFormula Copy(ParsedFormula formula, int rowDelta, int columnDelta)
    {
        if (formula == null)
        {
            throw new ArgumentNullException(nameof(formula));
        }

        if (rowDelta == 0 && columnDelta == 0)
        {
            return formula;
        }

        return new ParsedFormula(formula.Text, Visit(formula.Root));

        FormulaNode Visit(FormulaNode node)
        {
            if (node.Kind == FormulaNodeKind.Cell)
            {
                long row = node.Row == 0 || node.AbsoluteRow ? node.Row : (long)node.Row + rowDelta;
                long column = node.Column == 0 || node.AbsoluteColumn ? node.Column : (long)node.Column + columnDelta;
                return (node.Row != 0 && row < 1) || row > 1048576 || (node.Column != 0 && column < 1) || column > 16384
                    ? new FormulaNode(FormulaNodeKind.Error, node.Span, "#REF!")
                    : node.WithReference(node.Sheet, (int)row, (int)column, node.AbsoluteRow, node.AbsoluteColumn);
            }

            return node.Children.Count == 0 ? node : node.WithChildren(node.Children.Select(Visit));
        }
    }

    /// <summary>Copies a formula, moving only relative reference coordinates.</summary>
    /// <param name="formula">Original formula.</param>
    /// <param name="rowDelta">Row displacement.</param>
    /// <param name="columnDelta">Column displacement.</param>
    /// <returns>The rewritten formula.</returns>
    public static string Copy(string formula, int rowDelta, int columnDelta)
    {
        return Transform(formula, node =>
        {
            if (node.Kind != FormulaNodeKind.Cell)
            {
                return node;
            }

            long row = node.Row == 0 || node.AbsoluteRow ? node.Row : (long)node.Row + rowDelta;
            long column = node.Column == 0 || node.AbsoluteColumn ? node.Column : (long)node.Column + columnDelta;
            if ((node.Row != 0 && row < 1) || row > 1048576 || (node.Column != 0 && column < 1) || column > 16384)
            {
                return new FormulaNode(FormulaNodeKind.Error, node.Span, "#REF!");
            }

            return node.WithReference(node.Sheet, (int)row, (int)column, node.AbsoluteRow, node.AbsoluteColumn);
        });
    }

    /// <summary>Moves references for inserted rows, including absolute references.</summary>
    /// <param name="formula">Original formula.</param>
    /// <param name="formulaSheet">Sheet containing the formula.</param>
    /// <param name="targetSheet">Sheet receiving rows.</param>
    /// <param name="startRow">First inserted row.</param>
    /// <param name="count">Number of inserted rows.</param>
    /// <returns>The rewritten formula.</returns>
    public static string InsertRows(string formula, string formulaSheet, string targetSheet, int startRow, int count)
    {
        if (startRow < 1 || count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startRow));
        }

        return Transform(formula, node =>
        {
            if (node.Kind != FormulaNodeKind.Cell || node.Row == 0 || node.Row < startRow || !string.Equals(node.Sheet ?? formulaSheet, targetSheet, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }

            long row = (long)node.Row + count;
            return row > 1048576 ? new FormulaNode(FormulaNodeKind.Error, node.Span, "#REF!") : node.WithReference(node.Sheet, (int)row, node.Column, node.AbsoluteRow, node.AbsoluteColumn);
        });
    }

    /// <summary>Transforms each node after its children and serializes the resulting tree.</summary>
    /// <param name="formula">Original formula.</param>
    /// <param name="transform">Node transform.</param>
    /// <returns>The transformed formula.</returns>
    public static string Transform(string formula, Func<FormulaNode, FormulaNode> transform)
    {
        if (transform == null)
        {
            throw new ArgumentNullException(nameof(transform));
        }

        var parsed = FormulaParser.Parse(formula);
        if (!parsed.Success)
        {
            throw new ArgumentException(parsed.Diagnostics[0].Code + ": " + parsed.Diagnostics[0].Message, nameof(formula));
        }

        FormulaNode Visit(FormulaNode node)
        {
            return transform(node.WithChildren(node.Children.Select(Visit)));
        }

        return (formula.StartsWith("=", StringComparison.Ordinal) ? "=" : string.Empty) + Serialize(Visit(parsed.Formula!.Root));
    }

    /// <summary>Serializes a tree using invariant syntax and explicit grouping.</summary>
    /// <param name="root">Tree to serialize.</param>
    /// <returns>Formula text without a leading equals sign.</returns>
    public static string Serialize(FormulaNode root)
    {
        if (root == null)
        {
            throw new ArgumentNullException(nameof(root));
        }

        switch (root.Kind)
        {
            case FormulaNodeKind.Number:
                return root.Number.ToString("R", CultureInfo.InvariantCulture);
            case FormulaNodeKind.Text:
                return "\"" + root.Text.Replace("\"", "\"\"") + "\"";
            case FormulaNodeKind.Boolean:
                return root.Boolean ? "TRUE" : "FALSE";
            case FormulaNodeKind.Error:
                return root.Text;
            case FormulaNodeKind.Missing:
                return string.Empty;
            case FormulaNodeKind.Name:
                return Qualifier(root.Sheet) + root.Text;
            case FormulaNodeKind.Cell:
                return Qualifier(root.Sheet) + Coordinate(root);
            case FormulaNodeKind.Range:
                if (root.Children.Any(child => child.Kind == FormulaNodeKind.Error))
                {
                    return "#REF!";
                }

                return Serialize(root.Children[0]) + ":" + Coordinate(root.Children[1]);
            case FormulaNodeKind.Function:
                return root.Text + "(" + string.Join(",", root.Children.Select(Serialize)) + ")";
            case FormulaNodeKind.Parenthesized:
                return "(" + Serialize(root.Children[0]) + ")";
            case FormulaNodeKind.Unary:
                return "(" + root.Text + Serialize(root.Children[0]) + ")";
            case FormulaNodeKind.Percent:
                return "(" + Serialize(root.Children[0]) + "%)";
            case FormulaNodeKind.Binary:
                return "(" + Serialize(root.Children[0]) + root.Text + Serialize(root.Children[1]) + ")";
            default:
                throw new ArgumentException("Unknown formula node.", nameof(root));
        }
    }

    private static string Coordinate(FormulaNode node)
    {
        string column = string.Empty;
        int value = node.Column;
        while (value > 0)
        {
            value--;
            column = (char)('A' + (value % 26)) + column;
            value /= 26;
        }

        return (node.Column > 0 && node.AbsoluteColumn ? "$" : string.Empty) + column + (node.Row > 0 && node.AbsoluteRow ? "$" : string.Empty) + (node.Row > 0 ? node.Row.ToString(CultureInfo.InvariantCulture) : string.Empty);
    }

    private static string Qualifier(string? sheet)
    {
        return sheet == null ? string.Empty : "'" + sheet.Replace("'", "''") + "'!";
    }
}
