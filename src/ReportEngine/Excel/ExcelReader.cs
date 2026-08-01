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
    public static CellStyle Convert(IXLCell cell)
    {
        var style = cell.Style;
        return new(new FontStyle(
            style.Font.FontName,
            style.Font.FontSize,
            style.Font.Bold,
            style.Font.Italic,
            style.Font.Underline != XLFontUnderlineValues.None,
            ToColor(style.Font.FontColor)),
            ToBackground(style.Fill),
            ToBorder(style.Border),
            ToHorizontalAlignment(style.Alignment.Horizontal),
            ToVerticalAlignment(style.Alignment.Vertical),
            style.Alignment.WrapText);
    }

    private static ReportColor? ToBackground(IXLFill fill) =>
        fill.PatternType == XLFillPatternValues.None ? null : ToColor(fill.BackgroundColor);

    private static BorderStyle? ToBorder(IXLBorder border)
    {
        var left = ToBorderSide(border.LeftBorder, border.LeftBorderColor);
        var top = ToBorderSide(border.TopBorder, border.TopBorderColor);
        var right = ToBorderSide(border.RightBorder, border.RightBorderColor);
        var bottom = ToBorderSide(border.BottomBorder, border.BottomBorderColor);
        return left is null && top is null && right is null && bottom is null
            ? null
            : new BorderStyle(left, top, right, bottom);
    }

    private static BorderSide? ToBorderSide(XLBorderStyleValues style, XLColor color) =>
        style == XLBorderStyleValues.None ? null : new BorderSide(ToBorderWidth(style), ToColor(color));

    private static double ToBorderWidth(XLBorderStyleValues style) => style switch
    {
        XLBorderStyleValues.Thick => 2,
        XLBorderStyleValues.Medium or XLBorderStyleValues.MediumDashed or
            XLBorderStyleValues.MediumDashDot or XLBorderStyleValues.MediumDashDotDot => 1,
        XLBorderStyleValues.Double => 0.75,
        _ => 0.5
    };

    private static ReportColor? ToColor(XLColor color) =>
        color.HasValue ? new ReportColor(color.Color.R, color.Color.G, color.Color.B, color.Color.A) : null;

    private static HorizontalAlignment ToHorizontalAlignment(XLAlignmentHorizontalValues value) => value switch
    {
        XLAlignmentHorizontalValues.Center or XLAlignmentHorizontalValues.CenterContinuous or
            XLAlignmentHorizontalValues.Distributed => HorizontalAlignment.Center,
        XLAlignmentHorizontalValues.Right => HorizontalAlignment.Right,
        _ => HorizontalAlignment.Left
    };

    private static VerticalAlignment ToVerticalAlignment(XLAlignmentVerticalValues value) => value switch
    {
        XLAlignmentVerticalValues.Center or XLAlignmentVerticalValues.Distributed => VerticalAlignment.Center,
        XLAlignmentVerticalValues.Bottom => VerticalAlignment.Bottom,
        _ => VerticalAlignment.Top
    };
}
