using ClosedXML.Excel;
using ClosedXML.Excel.Drawings;
using ExcelRenderer.Fonts;
using ExcelRenderer.Model;
using ExcelRenderer.Rendering;
using SkiaSharp;

namespace ExcelRenderer.Excel;

/// <summary>Excel ブックのワークシート、セル、印刷設定、画像、および図形をレンダリング用モデルとして読み込みます。</summary>
public sealed class ExcelReader
{
    private readonly Dictionary<NormalFontMetadata, double> maximumDigitWidths = new();
    private readonly IFontManager? fontManager;

    /// <summary>Initializes a new instance of the <see cref="ExcelReader"/> class.</summary>
    public ExcelReader()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ExcelReader"/> class.</summary>
    /// <param name="fontManager">Normal スタイルの列幅計測に使用するフォントマネージャーです。</param>
    public ExcelReader(IFontManager fontManager)
    {
        this.fontManager = fontManager ?? throw new ArgumentNullException(nameof(fontManager));
    }

    /// <summary>指定した Excel ファイルを読み取り、各ワークシートの内容をレンダリング用ドキュメントへ変換します。</summary>
    /// <param name="path">読み取る Excel ファイルのパスです。</param>
    /// <returns>ブック内のワークシートを元の順序で格納したレンダリング用ドキュメントを返します。</returns>
    public ReportDocument Read(string path)
    {
        if (path is null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        using var input = File.OpenRead(path);
        return Read(input);
    }

    /// <summary>ストリームの現在位置からブックを読み取り、呼び出し元が所有するストリームを閉じずに処理します。</summary>
    /// <param name="input">読み取り対象の Excel データを含むストリームです。</param>
    /// <returns>ブック内のワークシートを元の順序で格納したレンダリング用ドキュメントを返します。</returns>
    public ReportDocument Read(Stream input)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        using var copy = new MemoryStream();
        input.CopyTo(copy);
        return Read(copy.ToArray(), null);
    }

    /// <summary>読み取り時の診断情報を収集しながら、バイト配列の Excel ブックを読み取ります。</summary>
    /// <param name="workbookBytes">Excel ブック全体を格納したバイト配列です。</param>
    /// <param name="diagnostics">読み取り中に発生した診断情報を追加するコレクターです。</param>
    /// <returns>ブック内のワークシートを元の順序で格納したレンダリング用ドキュメントを返します。</returns>
    internal ReportDocument Read(byte[] workbookBytes, DiagnosticCollector? diagnostics)
    {
        using var workbookStream = new MemoryStream(workbookBytes, writable: false);
        using var drawingStream = new MemoryStream(workbookBytes, writable: false);
        using var metadataStream = new MemoryStream(workbookBytes, writable: false);
        using var workbook = new XLWorkbook(workbookStream);
        var shapes = DrawingMLReader.Read(drawingStream, diagnostics);
        var pageSetups = WorkbookLayoutMetadataReader.ReadPageSetups(metadataStream);
        return new(workbook.Worksheets.Select(sheet => ReadSheet(
            sheet,
            shapes.GetValueOrDefault(sheet.Name, Array.Empty<ReportShape>()),
            pageSetups.GetValueOrDefault(sheet.Name),
            diagnostics,
            fontManager,
            maximumDigitWidths)).ToArray());
    }

    private static ReportSheet ReadSheet(
        IXLWorksheet worksheet,
        IReadOnlyList<ReportShape> shapes,
        SheetPageSetupMetadata? pageSetupMetadata,
        DiagnosticCollector? diagnostics,
        IFontManager? fontManager,
        Dictionary<NormalFontMetadata, double> maximumDigitWidths)
    {
        var cells = new Dictionary<CellAddress, ReportCell>();
        var columns = new Dictionary<int, ColumnDefinition>();
        var rows = new Dictionary<int, RowDefinition>();
        var usedRange = worksheet.RangeUsed(XLCellsUsedOptions.All);
        if (usedRange is not null)
        {
            foreach (var cell in usedRange.CellsUsed(XLCellsUsedOptions.All))
            {
                var address = new CellAddress(cell.Address.RowNumber, cell.Address.ColumnNumber);
                cells[address] = new(
                    cell.GetFormattedString(),
                    ExcelStyleConverter.Convert(cell),
                    Formula: cell.HasFormula ? "=" + cell.FormulaA1 : null);
            }

            for (var column = usedRange.RangeAddress.FirstAddress.ColumnNumber;
                 column <= usedRange.RangeAddress.LastAddress.ColumnNumber; column++)
            {
                var source = worksheet.Column(column);
                columns[column] = ReadColumnDefinition(
                    column, source, pageSetupMetadata, diagnostics, worksheet.Name, fontManager, maximumDigitWidths);
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
                    columns[column] = ReadColumnDefinition(
                        column, source, pageSetupMetadata, diagnostics, worksheet.Name, fontManager, maximumDigitWidths);
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

        var images = worksheet.Pictures.Select((picture, index) => ReadImage(picture, index)).ToArray();
        foreach (var image in images)
        {
            if (!columns.ContainsKey(image.Anchor.Column))
            {
                var source = worksheet.Column(image.Anchor.Column);
                columns[image.Anchor.Column] = ReadColumnDefinition(
                    image.Anchor.Column,
                    source,
                    pageSetupMetadata,
                    diagnostics,
                    worksheet.Name,
                    fontManager,
                    maximumDigitWidths);
            }

            if (!rows.ContainsKey(image.Anchor.Row))
            {
                var source = worksheet.Row(image.Anchor.Row);
                rows[image.Anchor.Row] = new(source.Height, source.IsHidden);
            }
        }

        foreach (var shape in shapes)
        {
            if (!columns.ContainsKey(shape.Anchor.Column))
            {
                var source = worksheet.Column(shape.Anchor.Column);
                columns[shape.Anchor.Column] = ReadColumnDefinition(
                    shape.Anchor.Column,
                    source,
                    pageSetupMetadata,
                    diagnostics,
                    worksheet.Name,
                    fontManager,
                    maximumDigitWidths);
            }

            if (!rows.ContainsKey(shape.Anchor.Row))
            {
                var source = worksheet.Row(shape.Anchor.Row);
                rows[shape.Anchor.Row] = new(source.Height, source.IsHidden);
            }
        }

        var sheet = new ReportSheet(
            worksheet.Name,
            cells,
            columns,
            rows,
            mergedRanges,
            ReadPageSettings(worksheet, pageSetupMetadata, diagnostics),
            ReadPrintArea(worksheet),
            images,
            ReadHeaderFooter(worksheet),
            shapes)
        {
            DefaultColumnWidth = GetDefaultColumnWidth(
                worksheet, pageSetupMetadata, diagnostics, fontManager, maximumDigitWidths),
            DefaultRowHeight = pageSetupMetadata?.DefaultRowHeight ?? worksheet.RowHeight,
        };
        return sheet;
    }

    private static ColumnDefinition ReadColumnDefinition(
        int column,
        IXLColumn source,
        SheetPageSetupMetadata? metadata,
        DiagnosticCollector? diagnostics,
        string sheetName,
        IFontManager? fontManager,
        Dictionary<NormalFontMetadata, double> maximumDigitWidths)
    {
        var raw = metadata?.Columns.LastOrDefault(definition =>
            column >= definition.First && column <= definition.Last);
        if (raw?.Width is not { } width)
        {
            return new(ExcelColumnWidthToPoints(source.Width), source.IsHidden);
        }

        var maximumDigitWidth = ResolveMaximumDigitWidth(
            metadata!, diagnostics, sheetName, fontManager, maximumDigitWidths);
        return new(ColumnWidthCalculator.ToPoints(width, maximumDigitWidth), raw.Hidden);
    }

    private static double GetDefaultColumnWidth(
        IXLWorksheet worksheet,
        SheetPageSetupMetadata? metadata,
        DiagnosticCollector? diagnostics,
        IFontManager? fontManager,
        Dictionary<NormalFontMetadata, double> maximumDigitWidths)
    {
        if (metadata?.DefaultColumnWidth is not { } width)
        {
            return ExcelColumnWidthToPoints(worksheet.ColumnWidth);
        }

        var maximumDigitWidth = ResolveMaximumDigitWidth(
            metadata, diagnostics, worksheet.Name, fontManager, maximumDigitWidths);
        return ColumnWidthCalculator.ToPoints(width, maximumDigitWidth);
    }

    private static double ResolveMaximumDigitWidth(
        SheetPageSetupMetadata metadata,
        DiagnosticCollector? diagnostics,
        string sheetName,
        IFontManager? fontManager,
        Dictionary<NormalFontMetadata, double> maximumDigitWidths)
    {
        if (fontManager is not null && metadata.NormalFont is { } normalFont)
        {
            try
            {
                if (!maximumDigitWidths.TryGetValue(normalFont, out var cached))
                {
                    var resolved = fontManager.Resolve(new(normalFont.Family));
                    using Stream stream = resolved.FontData is null
                        ? File.OpenRead(resolved.FilePath)
                        : new MemoryStream(resolved.FontData, writable: false);
                    using var typeface = SKTypeface.FromStream(stream) ??
                        throw new InvalidOperationException($"Font {resolved.Family} could not be loaded.");
                    using var font = new SKFont(typeface, (float)(normalFont.Size * 96 / 72));
                    cached = Math.Max(1, Math.Round(
                        Enumerable.Range(0, 10).Max(digit => font.MeasureText(digit.ToString())),
                        MidpointRounding.AwayFromZero));
                    maximumDigitWidths[normalFont] = cached;
                    diagnostics?.Add(new(
                        "MaximumDigitWidthResolved",
                        DiagnosticSeverity.Info,
                        DiagnosticStage.Read,
                        $"Normal font '{normalFont.Family}' resolved to '{resolved.Family}' with MDW {cached}px.",
                        sheetName));
                }

                return cached;
            }
            catch (Exception error) when (error is IOException or InvalidOperationException)
            {
                // Fall through to the documented compatibility metric.
            }
        }

        diagnostics?.Add(new(
            "MaximumDigitWidthFallback",
            DiagnosticSeverity.Warning,
            DiagnosticStage.Read,
            $"Normal font '{metadata.NormalFont?.Family ?? "unknown"}' could not be measured; the 7px compatibility metric was used.",
            sheetName));
        return 7;
    }

    private static CellRange? ReadPrintArea(IXLWorksheet worksheet)
    {
        var range = worksheet.PageSetup.PrintAreas.FirstOrDefault();
        return range is null ? null : new(
            new(range.RangeAddress.FirstAddress.RowNumber, range.RangeAddress.FirstAddress.ColumnNumber),
            new(range.RangeAddress.LastAddress.RowNumber, range.RangeAddress.LastAddress.ColumnNumber));
    }

    private static PageSettings ReadPageSettings(
        IXLWorksheet worksheet,
        SheetPageSetupMetadata? metadata,
        DiagnosticCollector? diagnostics)
    {
        var pageSetup = worksheet.PageSetup;
        var (width, height) = GetPaperSize(pageSetup.PaperSize);
        if (pageSetup.PageOrientation == XLPageOrientation.Landscape)
        {
            (width, height) = (height, width);
        }

        var margins = pageSetup.Margins;
        if (metadata is { FitToPage: true, FitToWidth: 0, FitToHeight: 0 })
        {
            diagnostics?.Add(new(
                "FitToPagesUnbounded",
                DiagnosticSeverity.Info,
                DiagnosticStage.Read,
                "Both fitToWidth and fitToHeight are zero; a 100% print scale will be used.",
                worksheet.Name));
        }

        var settings = new PageSettings(
            width,
            height,
            InchesToPoints(margins.Left),
            InchesToPoints(margins.Top),
            InchesToPoints(margins.Right),
            InchesToPoints(margins.Bottom),
            metadata?.FitToPage == true ? null : (metadata?.Scale ?? 100) / 100d,
            metadata?.FitToPage == true ? (int)(metadata.FitToWidth ?? 1) : null,
            metadata?.FitToPage == true ? (int)(metadata.FitToHeight ?? 1) : null,
            ReadRange(pageSetup.FirstRowToRepeatAtTop, pageSetup.LastRowToRepeatAtTop),
            ReadRange(pageSetup.FirstColumnToRepeatAtLeft, pageSetup.LastColumnToRepeatAtLeft))
        {
            ScaleMode = metadata?.FitToPage == true ? PrintScaleMode.FitToPages : PrintScaleMode.Explicit,
        };
        return settings;

        static IndexRange? ReadRange(int first, int last) =>
            first > 0 && last >= first ? new(first, last) : null;
    }

    private static (double Width, double Height) GetPaperSize(XLPaperSize paperSize) => paperSize switch
    {
        XLPaperSize.LetterPaper or XLPaperSize.LetterSmallPaper => (612, 792),
        XLPaperSize.LegalPaper => (612, 1008),
        XLPaperSize.A3Paper => (841.89, 1190.55),
        XLPaperSize.A5Paper => (419.53, 595.28),
        XLPaperSize.B4Paper => (708.66, 1000.63),
        XLPaperSize.B5Paper => (498.9, 708.66),
        _ => (595.276, 841.89),
    };

    private static HeaderFooter? ReadHeaderFooter(IXLWorksheet worksheet)
    {
        var pageSetup = worksheet.PageSetup;
        var header = ReadHeaderFooterSection(pageSetup.Header);
        var footer = ReadHeaderFooterSection(pageSetup.Footer);
        var firstHeader = pageSetup.DifferentFirstPageOnHF
            ? EmptyToNull(ReadHeaderFooterSection(pageSetup.Header, XLHFOccurrence.FirstPage)) : null;
        var firstFooter = pageSetup.DifferentFirstPageOnHF
            ? EmptyToNull(ReadHeaderFooterSection(pageSetup.Footer, XLHFOccurrence.FirstPage)) : null;
        var evenHeader = pageSetup.DifferentOddEvenPagesOnHF
            ? EmptyToNull(ReadHeaderFooterSection(pageSetup.Header, XLHFOccurrence.EvenPages)) : null;
        var evenFooter = pageSetup.DifferentOddEvenPagesOnHF
            ? EmptyToNull(ReadHeaderFooterSection(pageSetup.Footer, XLHFOccurrence.EvenPages)) : null;
        return header == new HeaderFooterSection() && footer == new HeaderFooterSection() &&
            firstHeader is null && firstFooter is null && evenHeader is null && evenFooter is null
            ? null
            : new(header, footer, firstHeader, firstFooter, evenHeader, evenFooter);

        static HeaderFooterSection? EmptyToNull(HeaderFooterSection section) =>
            section == new HeaderFooterSection() ? null : section;
    }

    private static HeaderFooterSection ReadHeaderFooterSection(
        IXLHeaderFooter headerFooter,
        XLHFOccurrence occurrence = XLHFOccurrence.OddPages) =>
        new(
            headerFooter.Left.GetText(occurrence),
            headerFooter.Center.GetText(occurrence),
            headerFooter.Right.GetText(occurrence));

    private static ReportImage ReadImage(IXLPicture picture, int zIndex)
    {
        var anchor = picture.TopLeftCell.Address;
        var offset = picture.GetOffset(XLMarkerPosition.TopLeft);
        return new(
            new CellAddress(anchor.RowNumber, anchor.ColumnNumber),
            PixelsToPoints(offset.X),
            PixelsToPoints(offset.Y),
            PixelsToPoints(picture.Width),
            PixelsToPoints(picture.Height),
            picture.ImageStream.ToArray(),
            zIndex,
            picture.Name);
    }

    private static double PixelsToPoints(int value) => value * 72d / 96d;

    private static double InchesToPoints(double value) => value * 72d;

    private static double ExcelColumnWidthToPoints(double value) => Math.Truncate((value * 7) + 5) * 72d / 96d;

    private static IReadOnlyList<CellBorder> ReadMergedBorders(Dictionary<CellAddress, ReportCell> cells, CellRange range)
    {
        var borders = new List<CellBorder>();
        foreach (var entry in cells.Where(entry => range.Contains(entry.Key)))
        {
            var source = entry.Value.Style.Border;
            if (source is null)
            {
                continue;
            }

            var address = entry.Key;
            var border = new BorderStyle(
                address.Column == range.First.Column ? source.Left : null,
                address.Row == range.First.Row ? source.Top : null,
                address.Column == range.Last.Column ? source.Right : null,
                address.Row == range.Last.Row ? source.Bottom : null);
            if (border != new BorderStyle())
            {
                borders.Add(new(address, border));
            }
        }

        return borders;
    }

    private static Dictionary<CellAddress, ReportCell> ApplyMergedSpans(
        Dictionary<CellAddress, ReportCell> cells, IEnumerable<CellRange> ranges)
    {
        foreach (var range in ranges)
        {
            var cell = cells.GetValueOrDefault(range.First, new(null, CellStyle.Default));
            cells[range.First] = cell with
            {
                RowSpan = range.Last.Row - range.First.Row + 1,
                ColumnSpan = range.Last.Column - range.First.Column + 1,
                Style = cell.Style with { Border = null },
                MergedBorders = ReadMergedBorders(cells, range),
            };
            foreach (var address in cells.Keys.Where(range.Contains).Where(address => address != range.First).ToArray())
            {
                cells.Remove(address);
            }
        }

        return cells;
    }
}
