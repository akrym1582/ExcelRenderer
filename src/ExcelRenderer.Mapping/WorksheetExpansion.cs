using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ClosedXML.Excel;

namespace ExcelRenderer.Mapping;

/// <summary>Pre-evaluates a worksheet, then expands it using ClosedXML row operations.</summary>
internal sealed class WorksheetExpansion
{
    private readonly TemplateSheet template;
    private readonly IXLWorksheet sheet;
    private readonly bool directPlacement;
    private readonly CancellationToken cancellationToken;
    private readonly List<ExpansionNode> nodes;
    private readonly List<int> sourceRows;
    private readonly HashSet<int> rowBreaks = new HashSet<int>();
    private readonly HashSet<int> columnBreaks;
    private readonly HashSet<int> originalRowBreaks;
    private bool explicitPageBreak;

    /// <summary>Initializes a new instance of the <see cref="WorksheetExpansion"/> class.</summary>
    /// <param name="template">The template.</param>
    /// <param name="worksheet">The legacy executor worksheet.</param>
    /// <param name="plan">The once-evaluated expansion.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    internal WorksheetExpansion(TemplateSheet template, IXLWorksheet worksheet, ExpansionPlan plan, CancellationToken cancellationToken)
    {
        this.template = template;
        sheet = worksheet;

        // Retain the established row-operation order when references or workbook objects may depend on it.
        directPlacement = sheet.Workbook.Worksheets.All(sheet =>
            !sheet.DefinedNames.Any() && !sheet.ConditionalFormats.Any() && !sheet.DataValidations.Any() &&
            !sheet.CellsUsed(XLCellsUsedOptions.Contents).Any(cell => cell.HasFormula)) &&
            !sheet.Workbook.DefinedNames.Any() &&
            !sheet.MergedRanges.Any() && !sheet.ConditionalFormats.Any() &&
            !sheet.DataValidations.Any() && !sheet.SparklineGroups.Any() &&
            !sheet.Tables.Any() && !sheet.PivotTables.Any() && !sheet.Pictures.Any() &&
            !sheet.AutoFilter.IsEnabled &&
            !sheet.PageSetup.PrintAreas.Any() &&
            sheet.PageSetup.FirstRowToRepeatAtTop == 0 &&
            !sheet.CellsUsed(XLCellsUsedOptions.All).Any(cell => cell.HasComment || cell.HasHyperlink || cell.HasRichText);
        this.cancellationToken = cancellationToken;
        nodes = plan.Nodes;
        sourceRows = Enumerable.Range(1, template.LastRow).ToList();
        originalRowBreaks = new HashSet<int>(sheet.PageSetup.RowBreaks);
        columnBreaks = new HashSet<int>(sheet.PageSetup.ColumnBreaks);
    }

    /// <summary>Gets the expanded worksheet name.</summary>
    internal string SheetName => sheet.Name;

    /// <summary>Gets a value indicating whether template directives enabled manual page breaks.</summary>
    internal bool HasExplicitPageBreak => explicitPageBreak;

    /// <summary>Applies the validated expansion to the worksheet.</summary>
    internal void Apply()
    {
        ApplyNodes(nodes, 1);
        for (var i = 0; i < sourceRows.Count; i++)
        {
            if (originalRowBreaks.Contains(sourceRows[i]))
            {
                rowBreaks.Add(i + 1);
            }
        }

        foreach (var original in originalRowBreaks.Where(r => r > template.LastRow))
        {
            var shifted = original + sourceRows.Count - template.LastRow;
            if (shifted > 0 && shifted <= 1_048_576)
            {
                rowBreaks.Add(shifted);
            }
        }

        var setup = sheet.PageSetup;
        setup.RowBreaks.Clear();
        setup.ColumnBreaks.Clear();
        foreach (var row in rowBreaks.OrderBy(r => r))
        {
            setup.AddHorizontalPageBreak(row);
        }

        foreach (var column in columnBreaks.OrderBy(c => c))
        {
            setup.AddVerticalPageBreak(column);
        }

        if (explicitPageBreak)
        {
            // Fit-to-page mode ignores manual breaks in Excel and in the renderer.
            setup.SetScale(setup.Scale > 0 ? setup.Scale : 100);
        }
    }

    private static void Flatten(ExpansionNode node, List<(TemplateNode Template, List<MappedValue> Values)> rows)
    {
        if (node.Template.IsBlock)
        {
            foreach (var block in node.Blocks)
            {
                foreach (var child in block)
                {
                    Flatten(child, rows);
                }
            }
        }
        else
        {
            foreach (var values in node.Rows)
            {
                rows.Add((node.Template, values));
            }
        }
    }

    private int ApplyNodes(List<ExpansionNode> plan, int start)
    {
        var row = start;
        foreach (var node in plan)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (directPlacement && node.Template.Path is not null)
            {
                row += ApplyRegion(node, row);
                continue;
            }

            var count = node.Template.LastRow - node.Template.FirstRow + 1;
            var copies = node.Template.IsBlock ? node.Blocks.Count : node.Rows.Count;
            if (copies == 0)
            {
                DeleteRows(row, count);
                continue;
            }

            DuplicateRows(row, count, copies, node.Template.Location);
            if (node.Template.IsBlock)
            {
                foreach (var block in node.Blocks)
                {
                    DeleteRows(row, 1);
                    var produced = ApplyNodes(block, row);
                    DeleteRows(row + produced, 1);
                    row += produced;
                }
            }
            else
            {
                foreach (var values in node.Rows)
                {
                    WriteValues(node.Template, values, row);
                    row++;
                }
            }
        }

        return row - start;
    }

    private int ApplyRegion(ExpansionNode node, int start)
    {
        var rows = new List<(TemplateNode Template, List<MappedValue> Values)>();
        Flatten(node, rows);
        var count = node.Template.LastRow - node.Template.FirstRow + 1;
        if ((long)sourceRows.Count + rows.Count - count > 1_048_576)
        {
            throw new MappingException(sheet.Name, node.Template.Location.Address, node.Template.Location.Text, "Intermediate expansion exceeds Excel's row limit.");
        }

        var originals = Enumerable.Range(start, count).Select(index =>
        {
            var source = sheet.Row(index);
            return new
            {
                source.Height,
                source.Style,
                source.OutlineLevel,
                source.IsHidden,
                Cells = source.CellsUsed(XLCellsUsedOptions.All).Select(cell => new
                {
                    Column = cell.Address.ColumnNumber,
                    cell.Value,
                    cell.Style,
                }).ToArray(),
            };
        }).ToArray();
        sheet.Range(start, 1, start + count - 1, template.LastColumn).Clear(XLClearOptions.All);
        var delta = rows.Count - count;
        if (delta > 0)
        {
            sheet.Row(start + count - 1).InsertRowsBelow(delta);
        }
        else if (delta < 0)
        {
            sheet.Rows(start + rows.Count, start + count - 1).Delete();
        }

        sourceRows.RemoveRange(start - 1, count);
        sourceRows.InsertRange(start - 1, rows.Select(row => row.Template.FirstRow));
        for (var index = 0; index < rows.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = start + index;
            var planned = rows[index];
            var original = originals[planned.Template.FirstRow - node.Template.FirstRow];
            var target = sheet.Row(row);
            target.Height = original.Height;
            target.Style = original.Style;
            target.OutlineLevel = original.OutlineLevel;
            if (original.IsHidden)
            {
                target.Hide();
            }
            else
            {
                target.Unhide();
            }

            foreach (var cell in original.Cells)
            {
                var destination = sheet.Cell(row, cell.Column);
                destination.Value = cell.Value;
                destination.Style = cell.Style;
            }

            WriteValues(planned.Template, planned.Values, row);
        }

        return rows.Count;
    }

    private void WriteValues(TemplateNode node, List<MappedValue> values, int row)
    {
        for (var i = 0; i < values.Count; i++)
        {
            var cell = node.Cells[i];
            sheet.Cell(row, cell.Column).Value = values[i].Value is { } value ? XLCellValue.FromObject(value) : Blank.Value;
            if (cell.Text == "**@page-break")
            {
                explicitPageBreak = true;
                if (row > 1)
                {
                    rowBreaks.Add(row - 1);
                }

                if (cell.Column > 1)
                {
                    columnBreaks.Add(cell.Column - 1);
                }
            }
        }
    }

    private void DuplicateRows(int start, int count, int copies, TemplateCell location)
    {
        if (copies == 1)
        {
            return;
        }

        var added = (long)count * (copies - 1);
        if (sourceRows.Count + added > 1_048_576)
        {
            throw new MappingException(sheet.Name, location.Address, location.Text, "Intermediate expansion exceeds Excel's row limit.");
        }

        var tags = sourceRows.GetRange(start - 1, count);
        sheet.Row(start + count - 1).InsertRowsBelow((int)added);
        for (var copy = 1; copy < copies; copy++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = start + (count * copy);
            for (var offset = 0; offset < count; offset++)
            {
                var source = sheet.Row(start + offset);
                var target = sheet.Row(destination + offset);
                target.Height = source.Height;
                target.Style = source.Style;
                target.OutlineLevel = source.OutlineLevel;
                if (source.IsHidden)
                {
                    target.Hide();
                }
                else
                {
                    target.Unhide();
                }
            }

            sheet.Range(start, 1, start + count - 1, template.LastColumn).CopyTo(sheet.Cell(destination, 1));
            sourceRows.InsertRange(destination - 1, tags);
        }
    }

    private void DeleteRows(int start, int count)
    {
        sheet.Rows(start, start + count - 1).Delete();
        sourceRows.RemoveRange(start - 1, count);
    }
}
