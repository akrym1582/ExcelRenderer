using ClosedXML.Excel;
using ReportEngine.Model;

namespace ReportEngine.Excel;

public sealed class ExcelReader
{
    public ReportDocument Read(string path)
    {
        using var workbook = new XLWorkbook(path);
        return new(workbook.Worksheets.Select(ReadSheet).ToArray());
    }

    private static ReportSheet ReadSheet(IXLWorksheet worksheet)
    {
        var cells = new Dictionary<CellAddress, ReportCell>();
        var columns = new Dictionary<int, ColumnDefinition>();
        var rows = new Dictionary<int, RowDefinition>();
        var usedRange = worksheet.RangeUsed();
        if (usedRange is not null)
        {
            foreach (var cell in usedRange.CellsUsed(XLCellsUsedOptions.All))
            {
                var address = new CellAddress(cell.Address.RowNumber, cell.Address.ColumnNumber);
                cells[address] = new(cell.GetFormattedString(), ExcelStyleConverter.Convert(cell));
            }

            for (var column = usedRange.RangeAddress.FirstAddress.ColumnNumber;
                 column <= usedRange.RangeAddress.LastAddress.ColumnNumber; column++)
            {
                var source = worksheet.Column(column);
                columns[column] = new(source.Width, source.IsHidden);
            }

            for (var row = usedRange.RangeAddress.FirstAddress.RowNumber;
                 row <= usedRange.RangeAddress.LastAddress.RowNumber; row++)
            {
                var source = worksheet.Row(row);
                rows[row] = new(source.Height, source.IsHidden);
            }
        }

        var mergedRanges = worksheet.MergedRanges.Select(range => new CellRange(
            new(range.RangeAddress.FirstAddress.RowNumber, range.RangeAddress.FirstAddress.ColumnNumber),
            new(range.RangeAddress.LastAddress.RowNumber, range.RangeAddress.LastAddress.ColumnNumber))).ToArray();
        cells = ApplyMergedSpans(cells, mergedRanges);

        return new(worksheet.Name, cells, columns, rows, mergedRanges, new PageSettings());
    }

    private static Dictionary<CellAddress, ReportCell> ApplyMergedSpans(
        Dictionary<CellAddress, ReportCell> cells, IEnumerable<CellRange> ranges)
    {
        foreach (var range in ranges)
        {
            if (cells.TryGetValue(range.First, out var cell))
                cells[range.First] = cell with
                {
                    RowSpan = range.Last.Row - range.First.Row + 1,
                    ColumnSpan = range.Last.Column - range.First.Column + 1
                };
        }
        return cells;
    }
}

public static class ExcelStyleConverter
{
    public static CellStyle Convert(IXLCell cell) =>
        new(new FontStyle(
            cell.Style.Font.FontName,
            cell.Style.Font.FontSize,
            cell.Style.Font.Bold,
            cell.Style.Font.Italic,
            cell.Style.Font.Underline != XLFontUnderlineValues.None),
            WrapText: cell.Style.Alignment.WrapText);
}
