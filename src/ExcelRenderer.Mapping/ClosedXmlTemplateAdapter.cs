using System;
using System.Linq;
using ClosedXML.Excel;
using ExcelRenderer.OpenXml;

namespace ExcelRenderer.Mapping;

/// <summary>Adapts legacy source structure and restrictions to the common template snapshot.</summary>
internal static class ClosedXmlTemplateAdapter
{
    /// <summary>Captures syntax inputs while preserving legacy workbook restriction checks.</summary>
    /// <param name="sheet">The original worksheet.</param>
    /// <returns>The template snapshot.</returns>
    internal static TemplateSnapshot Create(IXLWorksheet sheet)
    {
        var result = new TemplateSnapshot
        {
            Name = sheet.Name,
            LastRow = sheet.LastRowUsed(XLCellsUsedOptions.All)?.RowNumber() ?? 0,
            LastColumn = Math.Max(sheet.LastColumnUsed(XLCellsUsedOptions.All)?.ColumnNumber() ?? 1, sheet.MergedRanges.Select(range => range.RangeAddress.LastAddress.ColumnNumber).DefaultIfEmpty(1).Max()),
            ValidateRepeatedRange = Validate,
        };
        foreach (var group in sheet.CellsUsed(XLCellsUsedOptions.Contents).GroupBy(cell => cell.Address.RowNumber))
        {
            result.ContentCounts[group.Key] = group.Count();
            result.TextCells[group.Key] = group.Where(cell => !cell.HasFormula && cell.DataType == XLDataType.Text)
                .Select(cell => new TemplateCell(cell.Address.ToStringRelative(), cell.Address.ColumnNumber, cell.GetString()))
                .Where((cell, index) => index == 0 || cell.Text.StartsWith("**", StringComparison.Ordinal) || cell.Text.StartsWith("\\**", StringComparison.Ordinal)).ToArray();
        }

        result.MergedRanges.AddRange(sheet.MergedRanges.Select(range => new CellRange(new CellAddress(range.RangeAddress.FirstAddress.RowNumber, range.RangeAddress.FirstAddress.ColumnNumber), new CellAddress(range.RangeAddress.LastAddress.RowNumber, range.RangeAddress.LastAddress.ColumnNumber))));
        return result;

        MappingException Error(TemplateCell cell, string message) => new MappingException(sheet.Name, cell.Address, cell.Text, message);

        void Validate(TemplateNode node)
        {
            var first = node.IsBlock ? node.FirstRow + 1 : node.FirstRow;
            var last = node.IsBlock ? node.LastRow - 1 : node.LastRow;
            if (first <= last)
            {
                var formula = sheet.Rows(first, last).CellsUsed(XLCellsUsedOptions.Contents).FirstOrDefault(cell => cell.HasFormula);
                if (formula is not null)
                {
                    throw new MappingException(sheet.Name, formula.Address.ToStringRelative(), "=" + formula.FormulaA1, "Formulas inside repeated regions are not supported; supply calculated values in the input.");
                }
            }

            var names = sheet.Workbook.DefinedNames.Concat(sheet.Workbook.Worksheets.SelectMany(item => item.DefinedNames));
            foreach (var name in names)
            {
                if (name.Ranges.Any(range => range.Worksheet == sheet &&
                    range.RangeAddress.FirstAddress.RowNumber <= node.LastRow && range.RangeAddress.LastAddress.RowNumber >= node.FirstRow))
                {
                    throw Error(node.Location, $"Defined name '{name.Name}' overlaps a repeated region ({name.RefersTo}).");
                }
            }

            foreach (var merged in sheet.MergedRanges)
            {
                var range = merged.RangeAddress;
                if (range.FirstAddress.RowNumber <= last && range.LastAddress.RowNumber >= first &&
                    (range.FirstAddress.RowNumber < first || range.LastAddress.RowNumber > last))
                {
                    throw Error(node.Location, "A merged range crosses a repeated region boundary.");
                }
            }

            if (sheet.Tables.Any(t => t.RangeAddress.FirstAddress.RowNumber <= node.LastRow && t.RangeAddress.LastAddress.RowNumber >= node.FirstRow))
            {
                throw Error(node.Location, "Excel tables overlapping repeated regions are not supported; use ordinary cells.");
            }

            if (sheet.Pictures.Any(p => p.TopLeftCell.Address.RowNumber <= node.LastRow && p.BottomRightCell.Address.RowNumber >= node.FirstRow))
            {
                throw Error(node.Location, "Pictures overlapping repeated regions are not supported.");
            }
        }
    }
}
