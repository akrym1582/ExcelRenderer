using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ExcelRenderer.Formulas.References;
using ExcelRenderer.Formulas.Syntax;
using ExcelRenderer.OpenXml.Model.Cells;

namespace ExcelRenderer.OpenXml.Calculation;

/// <summary>Expands saved shared-formula references without evaluating any formula.</summary>
public static class FormulaTextResolver
{
    /// <summary>Resolves a saved formula, translating relative A1 references in shared followers.</summary>
    /// <param name="cell">The original source snapshot.</param>
    /// <param name="text">Formula text without the equals sign, or null when unavailable.</param>
    /// <returns>Whether the saved text can be represented faithfully.</returns>
    public static bool TryResolve(CellRecord cell, out string? text)
    {
        if (cell == null)
        {
            throw new ArgumentNullException(nameof(cell));
        }

        var formula = cell.Formula;
        text = formula?.RawFormula;
        if (formula == null || text!.Length > 0)
        {
            return true;
        }

        if (formula.Kind != "shared" || formula.MasterAddress == null || formula.MasterFormula == null)
        {
            text = null;
            return false;
        }

        return TryTranslate(formula.MasterFormula, cell.Address.Row - formula.MasterAddress.Value.Row, cell.Address.Column - formula.MasterAddress.Value.Column, out text);
    }

    private static bool TryTranslate(string source, int rowDelta, int columnDelta, out string? text)
    {
        var parsed = FormulaParser.Parse(source);
        if (!parsed.Success)
        {
            text = null;
            return false;
        }

        var references = new List<FormulaNode>();
        Collect(parsed.Formula!.Root, references);
        var result = new StringBuilder(source);
        foreach (var node in references.OrderByDescending(node => node.Span.Start))
        {
            var coordinateStart = node.Span.Start + node.Span.Length - node.Text.Length;
            long row = node.Row == 0 || node.AbsoluteRow ? node.Row : (long)node.Row + rowDelta;
            long column = node.Column == 0 || node.AbsoluteColumn ? node.Column : (long)node.Column + columnDelta;
            if ((node.Row != 0 && row < 1) || row > 1048576 || (node.Column != 0 && column < 1) || column > 16384)
            {
                text = null;
                return false;
            }

            var translated = FormulaRewriter.Serialize(node.WithReference(null, (int)row, (int)column, node.AbsoluteRow, node.AbsoluteColumn));
            result.Remove(coordinateStart, node.Text.Length).Insert(coordinateStart, translated);
        }

        text = result.ToString();
        return true;
    }

    private static void Collect(FormulaNode node, ICollection<FormulaNode> references)
    {
        if (node.Kind == FormulaNodeKind.Cell)
        {
            references.Add(node);
        }

        foreach (var child in node.Children)
        {
            Collect(child, references);
        }
    }
}
