using System.Globalization;
using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.Core.Model;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.Core.Excel;

/// <summary>Reads sparse original formula results without materializing worksheet DOMs.</summary>
internal static class FormulaCacheReader
{
    /// <summary>Reads result presence and original typed values before any evaluation.</summary>
    /// <param name="input">An independent source cursor.</param>
    /// <param name="workbook">The already loaded source workbook.</param>
    /// <param name="token">The cancellation token.</param>
    /// <returns>Formula-only cache metadata by sheet and address.</returns>
    internal static IReadOnlyDictionary<string, IReadOnlyDictionary<CellAddress, FormulaCache>> Read(Stream input, XLWorkbook workbook, CancellationToken token)
    {
        using var document = SpreadsheetDocument.Open(input, false);
        var part = document.WorkbookPart!;
        var result = new Dictionary<string, IReadOnlyDictionary<CellAddress, FormulaCache>>(StringComparer.Ordinal);
        foreach (var sheet in part.Workbook.Sheets!.Elements<S.Sheet>())
        {
            token.ThrowIfCancellationRequested();
            if (part.GetPartById(sheet.Id!) is not WorksheetPart worksheetPart || !workbook.TryGetWorksheet(sheet.Name!, out var worksheet))
            {
                continue;
            }

            var values = new Dictionary<CellAddress, FormulaCache>();
            var arrays = new List<IXLRangeAddress>();
            using var reader = OpenXmlReader.Create(worksheetPart);
            while (reader.Read())
            {
                if (!reader.IsStartElement || reader.ElementType != typeof(S.Cell))
                {
                    continue;
                }

                token.ThrowIfCancellationRequested();
                var cell = (S.Cell)reader.LoadCurrentElement()!;
                var reference = cell.CellReference?.Value;
                if (reference is null)
                {
                    continue;
                }

                var source = worksheet.Cell(reference);
                if (cell.CellFormula?.FormulaType?.Value == S.CellFormulaValues.Array && cell.CellFormula.Reference?.Value is { } range)
                {
                    arrays.Add(worksheet.Range(range).RangeAddress);
                }

                if (cell.CellFormula is null && !arrays.Any(array => array.FirstAddress.RowNumber <= source.Address.RowNumber &&
                    array.LastAddress.RowNumber >= source.Address.RowNumber && array.FirstAddress.ColumnNumber <= source.Address.ColumnNumber && array.LastAddress.ColumnNumber >= source.Address.ColumnNumber))
                {
                    continue;
                }

                var original = source.CachedValue;
                if (original.IsDateTime && double.TryParse(cell.CellValue?.InnerText, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial))
                {
                    // Format the original serial directly. ClosedXML loading converts dates before
                    // applying the 1904 offset, which shifts early serials across the 1900 leap bug.
                    original = serial + (workbook.Use1904DateSystem ? 1462 : 0);
                }

                values[new(source.Address.RowNumber, source.Address.ColumnNumber)] = new(
                    cell.CellValue is not null || cell.InlineString is not null,
                    cell.DataType?.InnerText,
                    cell.CellValue?.InnerText ?? cell.InlineString?.InnerText,
                    original);
            }

            result[sheet.Name!] = values;
        }

        return result;
    }
}
