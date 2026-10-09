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
    private readonly object? root;
    private readonly MappingOptions options;
    private readonly CancellationToken cancellationToken;
    private readonly List<ExpansionNode> nodes;
    private readonly List<int> sourceRows;
    private readonly HashSet<int> rowBreaks = new HashSet<int>();
    private readonly HashSet<int> columnBreaks;
    private readonly HashSet<int> originalRowBreaks;
    private int outputRows;
    private bool explicitPageBreak;

    /// <summary>Initializes a new instance of the <see cref="WorksheetExpansion"/> class.</summary>
    /// <param name="template">The template.</param>
    /// <param name="root">The root.</param>
    /// <param name="options">The options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    internal WorksheetExpansion(TemplateSheet template, object? root, MappingOptions options, CancellationToken cancellationToken)
    {
        this.template = template;
        this.root = root;
        this.options = options;
        this.cancellationToken = cancellationToken;
        nodes = Plan(template.Nodes, new Dictionary<string, object?>(StringComparer.Ordinal));
        sourceRows = Enumerable.Range(1, template.LastRow).ToList();
        originalRowBreaks = new HashSet<int>(template.Sheet.PageSetup.RowBreaks);
        columnBreaks = new HashSet<int>(template.Sheet.PageSetup.ColumnBreaks);
    }

    /// <summary>Gets the expanded worksheet name.</summary>
    internal string SheetName => template.Sheet.Name;

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

        var setup = template.Sheet.PageSetup;
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

    private List<ExpansionNode> Plan(List<TemplateNode> templates, Dictionary<string, object?> aliases)
    {
        var result = new List<ExpansionNode>();
        foreach (var node in templates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var planned = new ExpansionNode(node);
            var items = node.Path is null ? new object?[] { null } :
                TemplateSheet.At(template.Sheet, node.Location, () => node.Path.Array(root, aliases, options.MaxOutputRows));
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (node.IsBlock)
                {
                    var nestedAliases = new Dictionary<string, object?>(aliases, StringComparer.Ordinal) { [node.Alias!] = item };
                    planned.Blocks.Add(Plan(node.Children!, nestedAliases));
                }
                else
                {
                    outputRows++;
                    if (outputRows > options.MaxOutputRows)
                    {
                        throw new MappingException(template.Sheet.Name, node.Location.Address, node.Location.Text, "Expansion exceeds MaxOutputRows.");
                    }

                    var values = new List<XLCellValue>();
                    foreach (var cell in node.Cells)
                    {
                        values.Add(TemplateSheet.At(template.Sheet, cell, () => Value(cell, aliases, item, node.Path is not null)));
                    }

                    planned.Rows.Add(values);
                }
            }

            result.Add(planned);
        }

        return result;
    }

    private XLCellValue Value(TemplateCell cell, IReadOnlyDictionary<string, object?> aliases, object? item, bool implicitArray)
    {
        if (cell.Expression is null)
        {
            return cell.Text == "**@page-break" ? Blank.Value : cell.Text.Substring(1);
        }

        var value = cell.Expression.Evaluate(root, aliases, options, item, implicitArray);
        if (value is null)
        {
            return Blank.Value;
        }

        if (value is not string && value is not bool && value is not DateTime && value is not TimeSpan &&
            value is not byte && value is not sbyte && value is not short && value is not ushort &&
            value is not int && value is not uint && value is not long && value is not ulong &&
            value is not float && value is not double && value is not decimal)
        {
            throw new InvalidOperationException("A cell requires a scalar Excel value; use format() for other IFormattable types.");
        }

        return XLCellValue.FromObject(value, options.Culture);
    }

    private int ApplyNodes(List<ExpansionNode> plan, int start)
    {
        var row = start;
        foreach (var node in plan)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
                    for (var i = 0; i < values.Count; i++)
                    {
                        var cell = node.Template.Cells[i];
                        template.Sheet.Cell(row, cell.Column).Value = values[i];
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

                    row++;
                }
            }
        }

        return row - start;
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
            throw new MappingException(template.Sheet.Name, location.Address, location.Text, "Intermediate expansion exceeds Excel's row limit.");
        }

        var tags = sourceRows.GetRange(start - 1, count);
        template.Sheet.Row(start + count - 1).InsertRowsBelow((int)added);
        for (var copy = 1; copy < copies; copy++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = start + (count * copy);
            for (var offset = 0; offset < count; offset++)
            {
                var source = template.Sheet.Row(start + offset);
                var target = template.Sheet.Row(destination + offset);
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

            template.Sheet.Range(start, 1, start + count - 1, template.LastColumn).CopyTo(template.Sheet.Cell(destination, 1));
            sourceRows.InsertRange(destination - 1, tags);
        }
    }

    private void DeleteRows(int start, int count)
    {
        template.Sheet.Rows(start, start + count - 1).Delete();
        sourceRows.RemoveRange(start - 1, count);
    }
}
