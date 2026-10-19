using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ExcelRenderer.Mapping.Diagnostics;
using ExcelRenderer.OpenXml.Reader;
using ExcelRenderer.OpenXml.Reader.Model.Cells;

namespace ExcelRenderer.Mapping.Templates;

/// <summary>Captures only syntax inputs from the streaming source.</summary>
internal sealed partial class TemplateSnapshot
{
    /// <summary>Captures source text and structural validation without ClosedXML.</summary>
    /// <param name="reader">The source reader.</param>
    /// <param name="sheetIndex">Original worksheet index.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <param name="allowFormulas">Whether formula references will be rewritten after expansion.</param>
    /// <returns>Source locations and compiled-language inputs.</returns>
    internal static TemplateSnapshot FromOpenXml(OpenXmlWorkbookReader reader, int sheetIndex, CancellationToken cancellationToken, bool allowFormulas = false)
    {
        var info = reader.ReadWorksheetInfo(sheetIndex, cancellationToken);
        var result = new TemplateSnapshot
        {
            Name = info.Sheet.Name,
            LastRow = Math.Max(info.ActualCellRange?.Last.Row ?? 0, info.MergedRanges.Select(range => range.Last.Row).DefaultIfEmpty(0).Max()),
            LastColumn = Math.Max(info.ActualCellRange?.Last.Column ?? 1, info.MergedRanges.Select(range => range.Last.Column).DefaultIfEmpty(1).Max()),
        };
        result.MergedRanges.AddRange(info.MergedRanges);
        var text = new Dictionary<int, List<TemplateCell>>();
        var formulas = new List<CellRecord>();
        foreach (var cell in reader.EnumerateCells(sheetIndex, cancellationToken))
        {
            if (cell.Value.Type != CellValueType.Blank || cell.Formula != null)
            {
                result.ContentCounts[cell.Address.Row] = result.ContentCounts.TryGetValue(cell.Address.Row, out var count) ? count + 1 : 1;
            }

            if (cell.Formula != null)
            {
                formulas.Add(cell);
            }
            else if (cell.Value.Type == CellValueType.String)
            {
                if (!text.TryGetValue(cell.Address.Row, out var cells))
                {
                    cells = new List<TemplateCell>();
                    text.Add(cell.Address.Row, cells);
                }

                var value = cell.Value.Text ?? string.Empty;
                if (cells.Count == 0 || value.StartsWith("**", StringComparison.Ordinal) || value.StartsWith("\\**", StringComparison.Ordinal))
                {
                    cells.Add(new TemplateCell(cell.Address.ToString(), cell.Address.Column, value));
                }
            }
        }

        foreach (var row in text)
        {
            result.TextCells.Add(row.Key, row.Value.OrderBy(cell => cell.Column).ToArray());
        }

        result.ValidateRepeatedRange = node =>
        {
            var first = node.IsBlock ? node.FirstRow + 1 : node.FirstRow;
            var last = node.IsBlock ? node.LastRow - 1 : node.LastRow;
            var formula = formulas.FirstOrDefault(cell => cell.Address.Row >= first && cell.Address.Row <= last);
            if (formula != null && !allowFormulas)
            {
                throw new MappingException(result.Name, formula.Address.ToString(), "=" + formula.Formula!.RawFormula, "Formulas inside repeated regions are not supported; supply calculated values in the input.");
            }

            if (info.MergedRanges.Any(range => range.First.Row <= last && range.Last.Row >= first && (range.First.Row < first || range.Last.Row > last)))
            {
                throw new MappingException(result.Name, node.Location.Address, node.Location.Text, "A merged range crosses a repeated region boundary.");
            }
        };
        return result;
    }
}
