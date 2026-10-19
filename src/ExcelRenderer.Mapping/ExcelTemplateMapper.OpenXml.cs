using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using ExcelRenderer.Mapping.Configuration;
using ExcelRenderer.Mapping.Expansion;
using ExcelRenderer.Mapping.Templates;
using ExcelRenderer.OpenXml.Calculation;
using ExcelRenderer.OpenXml.Reader;
using ExcelRenderer.OpenXml.Reader.Input;
using ExcelRenderer.OpenXml.Writing;
using ExcelRenderer.OpenXml.Writing.Plans;

namespace ExcelRenderer.Mapping;

/// <summary>Connects the common language plan to the source-neutral workbook writer.</summary>
public static partial class ExcelTemplateMapper
{
    private static void MapOpenXml(PreparedWorkbook source, System.IO.Stream output, object? data, MappingOptions options, CancellationToken token)
    {
        using var input = source.OpenRead();
        using var reader = OpenXmlWorkbookReader.Open(input, cancellationToken: token);
        var templates = reader.SelectedWorksheetIndices.Select(index => (Index: index, Template: new TemplateSheet(TemplateSnapshot.FromOpenXml(reader, index, token, options.FormulaCalculationMode == FormulaCalculationMode.Recalculate)))).ToArray();
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
            }
        }

        if (reasons.Count > 0)
        {
            throw new NotSupportedException("OpenXml mapping is unsupported: " + string.Join(", ", reasons));
        }

        ValidateStructures(source, templates.Select(item => item.Template).ToArray(), token, options.FormulaCalculationMode == FormulaCalculationMode.Recalculate);
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
        using var writerInput = source.OpenRead();
        if (options.FormulaCalculationMode == FormulaCalculationMode.Recalculate)
        {
            using var mapped = new SpillableBufferStream(new InputBufferOptions { MemoryThresholdBytes = options.Input.MemoryThresholdBytes, AllowTemporaryFiles = options.Input.AllowTemporaryFiles, TemporaryDirectory = options.Input.TemporaryDirectory });
            OpenXmlWorkbookWriter.Write(writerInput, mapped, new WorkbookEditPlan(sheets.AsReadOnly(), rewriteFormulas: true), options.Input, token);
            mapped.Position = 0;
            if (options.RecalculateMappedWorkbook)
            {
                WorkbookRecalculator.Recalculate(mapped, output, options: options.FormulaEvaluationOptions, cancellationToken: token);
            }
            else
            {
                mapped.CopyTo(output);
            }
        }
        else
        {
            OpenXmlWorkbookWriter.Write(writerInput, output, new WorkbookEditPlan(sheets.AsReadOnly()), options.Input, token);
        }
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
