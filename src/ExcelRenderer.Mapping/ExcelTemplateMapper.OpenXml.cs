using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using ExcelRenderer.OpenXml;
using ExcelRenderer.OpenXml.Input;

namespace ExcelRenderer.Mapping;

/// <summary>Connects the common language plan to the source-neutral workbook writer.</summary>
public static partial class ExcelTemplateMapper
{
    private static bool MapOpenXml(PreparedWorkbook source, System.IO.Stream output, object? data, MappingOptions options, CancellationToken token)
    {
        using var input = source.OpenRead();
        using var reader = OpenXmlWorkbookReader.Open(input, cancellationToken: token);
        var templates = reader.SelectedWorksheetIndices.Select(index => (Index: index, Template: new TemplateSheet(TemplateSnapshot.FromOpenXml(reader, index, token)))).ToArray();
        foreach (var item in templates)
        {
            item.Template.ValidateRepeatedRanges();
            item.Template.Sheet.ValidateRepeatedRange = _ => { };
            item.Template.Sheet.TextCells.Clear();
            item.Template.Sheet.ContentCounts.Clear();
        }

        var reasons = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in templates)
        {
            var info = reader.ReadWorksheetInfo(item.Index, token);
            var repeated = Repeated(item.Template.Nodes).ToArray();
            foreach (var reference in info.PrintReferences)
            {
                if (reference.Name == "_xlnm.Print_Titles" && reference.Ranges.Any(range => range.Kind != "Columns" && repeated.Any(node => node.FirstRow <= range.LastRow && node.LastRow >= range.FirstRow)))
                {
                    reasons.Add("TitleRowsIntersectRepeat");
                }

                if (options.Backend == MappingBackend.Auto && reference.Name == "_xlnm.Print_Area" && reference.Ranges.Any(range => repeated.Any(node => node.FirstRow <= range.LastRow && node.LastRow >= range.FirstRow)))
                {
                    reasons.Add("PrintAreaExpansionCompatibility");
                }
            }
        }

        if (reasons.Count > 0)
        {
            if (options.Backend == MappingBackend.OpenXml)
            {
                throw new NotSupportedException("OpenXml mapping is unsupported: " + string.Join(", ", reasons));
            }

            options.BackendSelected?.Invoke(new MappingBackendSelection(options.Backend, MappingBackend.ClosedXml, Array.AsReadOnly(reasons.OrderBy(reason => reason, StringComparer.Ordinal).ToArray())));
            return false;
        }

        var plans = templates.Select(item => (item.Index, item.Template, Plan: new ExpansionPlan(item.Template, data, options, token))).ToArray();
        var sheets = new List<SheetEditPlan>();
        var nextOccurrence = 0;
        foreach (var item in plans)
        {
            var rows = new List<OutputRowOccurrence>();
            var rowBreaks = new HashSet<int>();
            var columnBreaks = new HashSet<int>();
            var explicitBreak = false;
            Flatten(item.Plan.Nodes, 0, 0);
            sheets.Add(new SheetEditPlan(item.Index, item.Template.LastRow, rows.AsReadOnly(), rowBreaks.OrderBy(value => value).ToArray(), columnBreaks.OrderBy(value => value).ToArray(), explicitBreak));

            void Flatten(List<ExpansionNode> nodes, int occurrenceId, int parentOccurrenceId)
            {
                foreach (var node in nodes)
                {
                    token.ThrowIfCancellationRequested();
                    if (node.Template.IsBlock)
                    {
                        foreach (var block in node.Blocks)
                        {
                            Flatten(block, checked(++nextOccurrence), occurrenceId);
                        }
                    }
                    else
                    {
                        foreach (var values in node.Rows)
                        {
                            var outputRow = checked(rows.Count + 1);
                            var edits = new List<CellValueEdit>();
                            for (var index = 0; index < values.Count; index++)
                            {
                                var cell = node.Template.Cells[index];
                                edits.Add(Edit(cell.Column, values[index].Value));
                                if (cell.Text == "**@page-break")
                                {
                                    explicitBreak = true;
                                    if (outputRow > 1)
                                    {
                                        rowBreaks.Add(outputRow - 1);
                                    }

                                    if (cell.Column > 1)
                                    {
                                        columnBreaks.Add(cell.Column - 1);
                                    }
                                }
                            }

                            var rowOccurrence = node.Template.Path is null ? occurrenceId : checked(++nextOccurrence);
                            rows.Add(new OutputRowOccurrence(node.Template.FirstRow, outputRow, rowOccurrence, node.Template.Path is null ? parentOccurrenceId : occurrenceId, edits.AsReadOnly()));
                        }
                    }
                }
            }
        }

        reader.Dispose();
        input.Dispose();
        options.BackendSelected?.Invoke(new MappingBackendSelection(options.Backend, MappingBackend.OpenXml, Array.AsReadOnly(new[] { options.Backend == MappingBackend.Auto ? "CapabilitiesSupported" : "ExplicitOpenXml" })));
        using var writerInput = source.OpenRead();
        OpenXmlWorkbookWriter.Write(writerInput, output, new WorkbookEditPlan(sheets.AsReadOnly()), options.Input, token);
        return true;
    }

    private static IEnumerable<TemplateNode> Repeated(List<TemplateNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.Path is not null)
            {
                yield return node;
            }

            foreach (var child in Repeated(node.Children ?? new List<TemplateNode>()))
            {
                yield return child;
            }
        }
    }

    private static CellValueEdit Edit(int column, object? value) => value switch
    {
        null => new CellValueEdit(column, (string?)null),
        string text => new CellValueEdit(column, text),
        bool boolean => new CellValueEdit(column, boolean),
        DateTime date => new CellValueEdit(column, date),
        TimeSpan duration => new CellValueEdit(column, duration),
        _ => new CellValueEdit(column, Convert.ToDouble(value, CultureInfo.InvariantCulture)),
    };
}
