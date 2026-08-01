using ClosedXML.Excel;
using ClosedXML.Excel.Drawings;
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
                columns[column] = new(ExcelColumnWidthToPoints(source.Width), source.IsHidden);
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

        foreach (var range in mergedRanges)
        {
            for (var column = range.First.Column; column <= range.Last.Column; column++)
            {
                if (!columns.ContainsKey(column))
                {
                    var source = worksheet.Column(column);
                    columns[column] = new(ExcelColumnWidthToPoints(source.Width), source.IsHidden);
                }
            }
            for (var row = range.First.Row; row <= range.Last.Row; row++)
            {
                if (!rows.ContainsKey(row))
                {
                    var source = worksheet.Row(row);
                    rows[row] = new(source.Height, source.IsHidden);
                }
            }
        }

        var images = worksheet.Pictures.Select(ReadImage).ToArray();

        return new(worksheet.Name, cells, columns, rows, mergedRanges, ReadPageSettings(worksheet),
            ReadPrintArea(worksheet), images, ReadHeaderFooter(worksheet));
    }

    private static CellRange? ReadPrintArea(IXLWorksheet worksheet)
    {
        var range = worksheet.PageSetup.PrintAreas.FirstOrDefault();
        return range is null ? null : new(
            new(range.RangeAddress.FirstAddress.RowNumber, range.RangeAddress.FirstAddress.ColumnNumber),
            new(range.RangeAddress.LastAddress.RowNumber, range.RangeAddress.LastAddress.ColumnNumber));
    }

    private static PageSettings ReadPageSettings(IXLWorksheet worksheet)
    {
        var pageSetup = worksheet.PageSetup;
        var (width, height) = GetPaperSize(pageSetup.PaperSize);
        if (pageSetup.PageOrientation == XLPageOrientation.Landscape)
            (width, height) = (height, width);

        var margins = pageSetup.Margins;
        return new(width, height,
            InchesToPoints(margins.Left), InchesToPoints(margins.Top),
            InchesToPoints(margins.Right), InchesToPoints(margins.Bottom));
    }

    private static (double Width, double Height) GetPaperSize(XLPaperSize paperSize) => paperSize switch
    {
        XLPaperSize.LetterPaper or XLPaperSize.LetterSmallPaper => (612, 792),
        XLPaperSize.LegalPaper => (612, 1008),
        XLPaperSize.A3Paper => (841.89, 1190.55),
        XLPaperSize.A5Paper => (419.53, 595.28),
        XLPaperSize.B4Paper => (708.66, 1000.63),
        XLPaperSize.B5Paper => (498.9, 708.66),
        _ => (595.276, 841.89)
    };

    private static HeaderFooter? ReadHeaderFooter(IXLWorksheet worksheet)
    {
        var header = ReadHeaderFooterSection(worksheet.PageSetup.Header);
        var footer = ReadHeaderFooterSection(worksheet.PageSetup.Footer);
        var firstHeader = ReadHeaderFooterSection(worksheet.PageSetup.Header, XLHFOccurrence.FirstPage);
        var firstFooter = ReadHeaderFooterSection(worksheet.PageSetup.Footer, XLHFOccurrence.FirstPage);
        var evenHeader = ReadHeaderFooterSection(worksheet.PageSetup.Header, XLHFOccurrence.EvenPages);
        var evenFooter = ReadHeaderFooterSection(worksheet.PageSetup.Footer, XLHFOccurrence.EvenPages);
        return header == new HeaderFooterSection() && footer == new HeaderFooterSection() &&
            firstHeader == new HeaderFooterSection() && firstFooter == new HeaderFooterSection() &&
            evenHeader == new HeaderFooterSection() && evenFooter == new HeaderFooterSection()
            ? null
            : new(header, footer, firstHeader, firstFooter, evenHeader, evenFooter);
    }

    private static HeaderFooterSection ReadHeaderFooterSection(
        IXLHeaderFooter headerFooter,
        XLHFOccurrence occurrence = XLHFOccurrence.OddPages) =>
        new(
            headerFooter.Left.GetText(occurrence),
            headerFooter.Center.GetText(occurrence),
            headerFooter.Right.GetText(occurrence));

    private static ReportImage ReadImage(IXLPicture picture)
    {
        var anchor = picture.TopLeftCell.Address;
        var offset = picture.GetOffset(XLMarkerPosition.TopLeft);
        return new(
            new CellAddress(anchor.RowNumber, anchor.ColumnNumber),
            PixelsToPoints(offset.X),
            PixelsToPoints(offset.Y),
            PixelsToPoints(picture.Width),
            PixelsToPoints(picture.Height),
            picture.ImageStream.ToArray());
    }

    private static double PixelsToPoints(int value) => value * 72d / 96d;
    private static double InchesToPoints(double value) => value * 72d;
    private static double ExcelColumnWidthToPoints(double value) => Math.Truncate(value * 7 + 5) * 72d / 96d;

    private static Dictionary<CellAddress, ReportCell> ApplyMergedSpans(
        Dictionary<CellAddress, ReportCell> cells, IEnumerable<CellRange> ranges)
    {
        foreach (var range in ranges)
        {
            var cell = cells.GetValueOrDefault(range.First, new(null, CellStyle.Default));
            cells[range.First] = cell with
            {
                RowSpan = range.Last.Row - range.First.Row + 1,
                ColumnSpan = range.Last.Column - range.First.Column + 1
            };
            foreach (var address in cells.Keys.Where(range.Contains).Where(address => address != range.First).ToArray())
                cells.Remove(address);
        }
        return cells;
    }
}
