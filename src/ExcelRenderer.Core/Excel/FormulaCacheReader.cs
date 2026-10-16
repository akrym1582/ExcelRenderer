using System.Globalization;
using ClosedXML.Excel;
using ExcelNumberFormat;
using ExcelRenderer.OpenXml;
using CellAddress = ExcelRenderer.Core.Model.CellAddress;

namespace ExcelRenderer.Core.Excel;

/// <summary>Reads sparse original formula results without consulting ClosedXML cell values.</summary>
internal static class FormulaCacheReader
{
    /// <summary>Reads result presence, typed values and original formula metadata.</summary>
    /// <param name="input">An independent source cursor.</param>
    /// <param name="token">The cancellation token.</param>
    /// <returns>Formula-only cache metadata by sheet and address.</returns>
    internal static IReadOnlyDictionary<string, IReadOnlyDictionary<CellAddress, FormulaCache>> Read(Stream input, CancellationToken token)
    {
        using var reader = OpenXmlWorkbookReader.Open(input, cancellationToken: token);
        var dateStyles = new Dictionary<int, bool>();
        var result = new Dictionary<string, IReadOnlyDictionary<CellAddress, FormulaCache>>(StringComparer.Ordinal);
        foreach (var sheet in reader.Sheets.Where(sheet => sheet.Kind == "Worksheet"))
        {
            token.ThrowIfCancellationRequested();
            var values = new Dictionary<CellAddress, FormulaCache>();
            foreach (var cell in reader.EnumerateCells(sheet.Index, token))
            {
                if (cell.Formula is not { } formula)
                {
                    continue;
                }

                XLCellValue value = cell.Value.Type switch
                {
                    CellValueType.Number => cell.Value.Number!.Value,
                    CellValueType.Boolean => cell.Value.Boolean!.Value,
                    CellValueType.String or CellValueType.Error => cell.Value.Text ?? string.Empty,
                    CellValueType.IsoDate => DateTime.Parse(cell.Value.Text!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                    _ => Blank.Value,
                };
                if (cell.Value.Type == CellValueType.Number && reader.Workbook.Date1904)
                {
                    if (!dateStyles.TryGetValue(cell.StyleIndex, out var isDate))
                    {
                        var format = new NumberFormat(reader.ResolveStyle(cell).NumberFormatCode ?? "General");
                        isDate = format.IsDateTimeFormat && !format.IsTimeSpanFormat;
                        if (dateStyles.Count < 8192)
                        {
                            dateStyles.Add(cell.StyleIndex, isDate);
                        }
                    }

                    if (isDate)
                    {
                        // Scratch formatting accepts 1900 serials; durations never receive a date offset.
                        value = cell.Value.Number!.Value + 1462;
                    }
                }

                values[new(cell.Address.Row, cell.Address.Column)] = new(formula.HasCachedResult, value, cell.Value.Type == CellValueType.Error, formula);
            }

            result[sheet.Name] = values;
        }

        return result;
    }
}
