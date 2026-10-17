using System;
using System.Linq;
using ExcelRenderer.Formulas.References;
using ExcelRenderer.Formulas.Syntax;
using ExcelRenderer.OpenXml.Model.Cells;
using ExcelRenderer.OpenXml.Writing.Plans;

namespace ExcelRenderer.OpenXml.References;

/// <summary>Transforms formula AST references using the indexed final row placement.</summary>
internal sealed partial class StructuralReferences
{
    /// <summary>Rewrites a cell formula, separating structural movement from copy offsets.</summary>
    /// <param name="text">Resolved ordinary formula.</param>
    /// <param name="owner">Original formula worksheet.</param>
    /// <param name="occurrence">Final formula row occurrence, or null for a defined name.</param>
    /// <param name="column">Formula column for diagnostics, or zero for a defined name.</param>
    /// <returns>Safely serialized formula.</returns>
    internal string RewriteFormula(string text, string owner, OutputRowOccurrence? occurrence = null, int column = 0)
    {
        var parsed = FormulaParser.Parse(text);
        if (parsed.Formula == null)
        {
            throw new NotSupportedException("Cannot rewrite formula at " + owner + (occurrence == null || column == 0 ? string.Empty : "!" + new CellAddress(occurrence.SourceRow, column)) + ": " + string.Join("; ", parsed.Diagnostics.Select(diagnostic => diagnostic.Code + " " + diagnostic.Message)) + " Formula: " + text);
        }

        sheets.TryGetValue(owner, out var ownerPlan);
        var delta = occurrence == null || ownerPlan == null ? 0 : occurrence.OutputRow - (FirstRow(ownerPlan, occurrence.SourceRow) ?? occurrence.OutputRow);
        return FormulaRewriter.Serialize(Visit(parsed.Formula.Root));

        FormulaNode Visit(FormulaNode node)
        {
            if (node.Kind == FormulaNodeKind.Range)
            {
                var first = node.Children[0];
                var last = node.Children[1];
                if (first.Row == 0 || !sheets.TryGetValue(first.Sheet ?? node.Sheet ?? owner, out var plan))
                {
                    return node;
                }

                var mapped = Range(plan, Math.Min(first.Row, last.Row), Math.Max(first.Row, last.Row));
                if (mapped == null)
                {
                    return FormulaParser.Parse("#REF!").Formula!.Root;
                }

                var ascending = first.Row <= last.Row;
                return node.WithChildren(new[] { Endpoint(first, ascending ? mapped.Value.First : mapped.Value.Last, plan), Endpoint(last, ascending ? mapped.Value.Last : mapped.Value.First, plan) });
            }

            if (node.Kind == FormulaNodeKind.Cell && node.Row != 0 && sheets.TryGetValue(node.Sheet ?? owner, out var target))
            {
                var row = FirstRow(target, node.Row);
                return row == null ? FormulaParser.Parse("#REF!").Formula!.Root : Endpoint(node, row.Value, target);
            }

            return node.Children.Count == 0 ? node : node.WithChildren(node.Children.Select(Visit));
        }

        FormulaNode Endpoint(FormulaNode node, int moved, SheetEditPlan plan)
        {
            var contextual = !node.AbsoluteRow && occurrence != null && plan == ownerPlan ? ContextRow(plan, node.Row, occurrence.OccurrenceId) : null;
            var row = contextual ?? checked(moved + (node.AbsoluteRow ? 0 : delta));
            return row is < 1 or > 1048576 ? FormulaParser.Parse("#REF!").Formula!.Root : node.WithReference(node.Sheet, row, node.Column, node.AbsoluteRow, node.AbsoluteColumn);
        }
    }
}
