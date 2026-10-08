using ClosedXML.Excel;
using ClosedXML.Excel.Drawings;
using ClosedXML.Graphics;
using ExcelRenderer.Slim.Fonts;
using ExcelRenderer.Slim.Input;
using ExcelRenderer.Slim.Model;
using ExcelRenderer.Slim.Rendering;
using SkiaSharp;

namespace ExcelRenderer.Slim.Excel;

/// <summary>Excel ブックのワークシート、セル、印刷設定、画像をレンダリング用モデルとして読み込みます。</summary>
internal sealed class ExcelReader
{
    private readonly SingleFontContext fontManager;
    private readonly Dictionary<NormalFontMetadata, double> maximumDigitWidths = new();

    /// <summary>Initializes a new instance of the <see cref="ExcelReader"/> class.</summary>
    /// <param name="fontManager">The single conversion font.</param>
    internal ExcelReader(SingleFontContext fontManager) => this.fontManager = fontManager;

    /// <summary>Reads workbook models with optional selected-sheet body projection.</summary>
    /// <param name="source">The source used by this operation.</param>
    /// <param name="diagnostics">The diagnostics used by this operation.</param>
    /// <param name="selectedSheets">The selectedSheets used by this operation.</param>
    /// <param name="cancellationToken">The reader cancellation token.</param>
    /// <returns>The planned or generated result.</returns>
    internal ReportDocument Read(PreparedWorkbook source, DiagnosticCollector? diagnostics, IReadOnlyList<string>? selectedSheets = null, CancellationToken cancellationToken = default)
    {
        using var workbookStream = source.OpenRead();
        using var pictureSource = source.OpenRead();
        using var pictureMetadataStream = new CancellationReadStream(pictureSource, cancellationToken);
        using var metadataSource = source.OpenRead();
        using var metadataStream = new CancellationReadStream(metadataSource, cancellationToken);
        using var cancelRead = new CancellationReadStream(workbookStream, cancellationToken);
        var graphics = new SingleFontGraphicEngine(fontManager);
        using var workbook = new XLWorkbook(cancelRead, new LoadOptions { GraphicEngine = graphics });
        if (selectedSheets is not null)
        {
            foreach (var name in selectedSheets)
            {
                if (string.IsNullOrEmpty(name) || !workbook.Worksheets.Any(sheet => sheet.Name == name))
                {
                    throw new ArgumentException($"Worksheet was not found: {name}", nameof(selectedSheets));
                }
            }
        }

        var pictureMetadata = DrawingMLReader.ReadPictureMetadata(pictureMetadataStream);
        var pageSetups = WorkbookLayoutMetadataReader.ReadPageSetups(metadataStream);
        var styles = new StylePool();
        return new ReportDocument(workbook.Worksheets.Where(sheet => selectedSheets is null || selectedSheets.Contains(sheet.Name, StringComparer.Ordinal)).Select((sheet, index) => ReadSheet(
            sheet,
            pictureMetadata.GetValueOrDefault(
                sheet.Name,
                new Dictionary<string, DrawingPictureMetadata>(StringComparer.Ordinal)),
            pageSetups.GetValueOrDefault(sheet.Name),
            diagnostics,
            fontManager,
            maximumDigitWidths,
            styles,
            graphics,
            cancellationToken)).ToArray());
    }

    private static ReportSheet ReadSheet(
        IXLWorksheet worksheet,
        IReadOnlyDictionary<string, DrawingPictureMetadata> pictureMetadata,
        SheetPageSetupMetadata? pageSetupMetadata,
        DiagnosticCollector? diagnostics,
        SingleFontContext fontManager,
        Dictionary<NormalFontMetadata, double> maximumDigitWidths,
        StylePool styles,
        IXLGraphicEngine graphics,
        CancellationToken cancellationToken)
    {
        var cells = new Dictionary<CellAddress, ReportCell>();
        var columns = new Dictionary<int, ColumnDefinition>();
        var rows = new Dictionary<int, RowDefinition>();
        var usedRange = worksheet.RangeUsed(XLCellsUsedOptions.All);
        if (usedRange is not null)
        {
            foreach (var cell in usedRange.CellsUsed(XLCellsUsedOptions.All))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var address = new CellAddress(cell.Address.RowNumber, cell.Address.ColumnNumber);
                cells[address] = new(
                    ReadDisplay(cell, graphics),
                    styles.Intern(ExcelStyleConverter.Convert(cell)),
                    Formula: cell.HasFormula ? "=" + cell.FormulaA1 : null);
            }

            for (var column = usedRange.RangeAddress.FirstAddress.ColumnNumber;
                 column <= usedRange.RangeAddress.LastAddress.ColumnNumber; column++)
            {
                var source = worksheet.Column(column);
                columns[column] = ReadColumnDefinition(
                    column,
                    source,
                    pageSetupMetadata,
                    diagnostics,
                    worksheet.Name,
                    fontManager,
                    maximumDigitWidths);
            }

            for (var row = usedRange.RangeAddress.FirstAddress.RowNumber;
                 row <= usedRange.RangeAddress.LastAddress.RowNumber; row++)
            {
                var source = worksheet.Row(row);
                rows[row] = new(source.Height, source.IsHidden);
            }
        }

        // Row/column dimensions before a used or printed range contribute to every drawing anchor's
        // sheet-origin coordinate. Keep XML overrides even when ClosedXML does not include them in RangeUsed.
        if (pageSetupMetadata is not null)
        {
            foreach (var raw in pageSetupMetadata.Columns)
            {
                for (var column = raw.First; column <= raw.Last && column <= 16_384; column++)
                {
                    if (!columns.ContainsKey(column))
                    {
                        columns[column] = ReadColumnDefinition(
                            column,
                            worksheet.Column(column),
                            pageSetupMetadata,
                            diagnostics,
                            worksheet.Name,
                            fontManager,
                            maximumDigitWidths);
                    }
                }
            }

            foreach (var raw in pageSetupMetadata.Rows)
            {
                if (!rows.ContainsKey(raw.Index))
                {
                    rows[raw.Index] = new(raw.Height ?? pageSetupMetadata.DefaultRowHeight, raw.Hidden);
                }
            }
        }

        var mergedRanges = worksheet.MergedRanges.Select(range => new CellRange(
            new(range.RangeAddress.FirstAddress.RowNumber, range.RangeAddress.FirstAddress.ColumnNumber),
            new(range.RangeAddress.LastAddress.RowNumber, range.RangeAddress.LastAddress.ColumnNumber))).ToArray();
        cells = ApplyMergedSpans(cells, mergedRanges, styles);

        var printAreas = ReadPrintAreas(worksheet);

        foreach (var range in mergedRanges)
        {
            for (var column = range.First.Column; column <= range.Last.Column; column++)
            {
                if (!columns.ContainsKey(column))
                {
                    var source = worksheet.Column(column);
                    columns[column] = ReadColumnDefinition(
                        column,
                        source,
                        pageSetupMetadata,
                        diagnostics,
                        worksheet.Name,
                        fontManager,
                        maximumDigitWidths);
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

        var images = worksheet.Pictures.Select((picture, index) => ReadImage(
            picture,
            pictureMetadata.GetValueOrDefault(picture.Name),
            index)).ToArray();
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

        var geometryRanges = printAreas.Concat(mergedRanges)
            .Concat(images.SelectMany(image => GetAnchorRanges(image.Anchor, image.DrawingAnchor)))
            .ToArray();
        if (geometryRanges.Length > 0)
        {
            var firstColumn = geometryRanges.Min(range => range.First.Column);
            var lastColumn = geometryRanges.Max(range => range.Last.Column);
            for (var column = firstColumn; column <= lastColumn; column++)
            {
                if (!columns.ContainsKey(column))
                {
                    columns[column] = ReadColumnDefinition(
                        column,
                        worksheet.Column(column),
                        pageSetupMetadata,
                        diagnostics,
                        worksheet.Name,
                        fontManager,
                        maximumDigitWidths);
                }
            }

            var firstRow = geometryRanges.Min(range => range.First.Row);
            var lastRow = geometryRanges.Max(range => range.Last.Row);
            for (var row = firstRow; row <= lastRow; row++)
            {
                if (!rows.ContainsKey(row))
                {
                    var source = worksheet.Row(row);
                    rows[row] = new(source.Height, source.IsHidden);
                }
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
            ReadHeaderFooter(worksheet))
        {
            DefaultColumnWidth = GetDefaultColumnWidth(
                worksheet,
                pageSetupMetadata,
                diagnostics,
                fontManager,
                maximumDigitWidths),
            DefaultRowHeight = pageSetupMetadata?.DefaultRowHeight ?? worksheet.RowHeight,
            PrintAreas = printAreas,
        };
        return sheet;

        static IEnumerable<CellRange> GetAnchorRanges(CellAddress fallback, DrawingAnchor? anchor)
        {
            var from = anchor?.From ?? fallback;
            var to = anchor?.To ?? from;
            yield return new(
                new(Math.Min(from.Row, to.Row), Math.Min(from.Column, to.Column)),
                new(Math.Max(from.Row, to.Row), Math.Max(from.Column, to.Column)));
        }
    }

    private static string ReadDisplay(IXLCell cell, IXLGraphicEngine graphics)
    {
        if (!cell.HasFormula || cell.FormulaA1.IndexOf("HYPERLINK", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return DisplayText.WithoutVariationSelectors(cell.GetFormattedString());
        }

        using var scratch = new XLWorkbook(new LoadOptions { GraphicEngine = graphics });
        var cached = scratch.AddWorksheet("cached").Cell(1, 1);
        cached.Value = cell.CachedValue;
        cached.Style = cell.Style;
        return DisplayText.WithoutVariationSelectors(cached.GetFormattedString());
    }

    private static ColumnDefinition ReadColumnDefinition(
        int column,
        IXLColumn source,
        SheetPageSetupMetadata? metadata,
        DiagnosticCollector? diagnostics,
        string sheetName,
        SingleFontContext fontManager,
        Dictionary<NormalFontMetadata, double> maximumDigitWidths)
    {
        var raw = metadata?.Columns.LastOrDefault(definition =>
            column >= definition.First && column <= definition.Last);
        var maximumDigitWidth = ResolveMaximumDigitWidth(
            metadata,
            diagnostics,
            sheetName,
            fontManager,
            maximumDigitWidths);
        var width = raw?.Width ?? metadata?.DefaultColumnWidth ??
            ColumnWidthCalculator.FromBaseColumnWidth(metadata?.BaseColumnWidth ?? 8, maximumDigitWidth);
        return new(ColumnWidthCalculator.ToPoints(width, maximumDigitWidth), raw?.Hidden ?? source.IsHidden);
    }

    private static double GetDefaultColumnWidth(
        IXLWorksheet worksheet,
        SheetPageSetupMetadata? metadata,
        DiagnosticCollector? diagnostics,
        SingleFontContext fontManager,
        Dictionary<NormalFontMetadata, double> maximumDigitWidths)
    {
        var maximumDigitWidth = ResolveMaximumDigitWidth(
            metadata,
            diagnostics,
            worksheet.Name,
            fontManager,
            maximumDigitWidths);
        var width = metadata?.DefaultColumnWidth ??
            ColumnWidthCalculator.FromBaseColumnWidth(metadata?.BaseColumnWidth ?? 8, maximumDigitWidth);
        return ColumnWidthCalculator.ToPoints(width, maximumDigitWidth);
    }

    private static double ResolveMaximumDigitWidth(
        SheetPageSetupMetadata? metadata,
        DiagnosticCollector? diagnostics,
        string sheetName,
        SingleFontContext fontManager,
        Dictionary<NormalFontMetadata, double> maximumDigitWidths)
    {
        var normalFont = metadata?.NormalFont ?? new NormalFontMetadata(SingleFontContext.Family, 11);
        if (!maximumDigitWidths.TryGetValue(normalFont, out var width))
        {
            width = fontManager.MaximumDigitWidth(normalFont.Size);
            maximumDigitWidths[normalFont] = width;
        }

        return width;
    }

    private static CellRange? ReadPrintArea(IXLWorksheet worksheet)
    {
        var areas = ReadPrintAreas(worksheet);
        return areas.Count == 0 ? null : areas[0];
    }

    private static IReadOnlyList<CellRange> ReadPrintAreas(IXLWorksheet worksheet) =>
        worksheet.PageSetup.PrintAreas.Select(range => new CellRange(
            new(range.RangeAddress.FirstAddress.RowNumber, range.RangeAddress.FirstAddress.ColumnNumber),
            new(range.RangeAddress.LastAddress.RowNumber, range.RangeAddress.LastAddress.ColumnNumber))).ToArray();

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
            ManualRowBreaks = metadata?.RowBreaks ?? [],
            ManualColumnBreaks = metadata?.ColumnBreaks ?? [],
            PageOrder = metadata?.PageOrder ?? PrintPageOrder.DownThenOver,
            HorizontalCentered = metadata?.HorizontalCentered ?? false,
            VerticalCentered = metadata?.VerticalCentered ?? false,
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
            DisplayText.WithoutVariationSelectors(headerFooter.Left.GetText(occurrence)),
            DisplayText.WithoutVariationSelectors(headerFooter.Center.GetText(occurrence)),
            DisplayText.WithoutVariationSelectors(headerFooter.Right.GetText(occurrence)));

    private static ReportImage ReadImage(
        IXLPicture picture,
        DrawingPictureMetadata? metadata,
        int zIndex)
    {
        var anchor = picture.TopLeftCell.Address;
        var offset = picture.GetOffset(XLMarkerPosition.TopLeft);
        var sourceAnchor = metadata?.Anchor;
        var cell = sourceAnchor?.From ?? new CellAddress(anchor.RowNumber, anchor.ColumnNumber);
        var offsetX = sourceAnchor?.Kind == DrawingAnchorKind.Absolute
            ? sourceAnchor.PositionX
            : sourceAnchor?.FromOffsetX ?? PixelsToPoints(offset.X);
        var offsetY = sourceAnchor?.Kind == DrawingAnchorKind.Absolute
            ? sourceAnchor.PositionY
            : sourceAnchor?.FromOffsetY ?? PixelsToPoints(offset.Y);
        var width = sourceAnchor is { ExtentWidth: > 0 } ? sourceAnchor.ExtentWidth : PixelsToPoints(picture.Width);
        var height = sourceAnchor is { ExtentHeight: > 0 } ? sourceAnchor.ExtentHeight : PixelsToPoints(picture.Height);
        return new ReportImage(
            cell,
            offsetX,
            offsetY,
            width,
            height,
            picture.ImageStream.ToArray(),
            metadata?.ZIndex ?? zIndex,
            picture.Name)
        {
            DrawingAnchor = sourceAnchor,
        };
    }

    private static double PixelsToPoints(int value) => value * 72d / 96d;

    private static double InchesToPoints(double value) => value * 72d;

    private static IReadOnlyList<CellBorder> ReadMergedBorders(IEnumerable<KeyValuePair<CellAddress, ReportCell>> entries, CellRange range, StylePool styles)
    {
        var borders = new List<CellBorder>();
        foreach (var entry in entries)
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
                borders.Add(new(address, styles.Intern(border)));
            }
        }

        return borders;
    }

    private static Dictionary<CellAddress, ReportCell> ApplyMergedSpans(
        Dictionary<CellAddress, ReportCell> cells,
        IEnumerable<CellRange> ranges,
        StylePool styles)
    {
        var merged = ranges.ToArray();
        var index = new CellRangeIndex(cells.Keys.Concat(merged.Select(range => range.First)).Distinct());
        foreach (var range in merged)
        {
            var entries = index.Query(range).Where(cells.ContainsKey)
                .Select(address => new KeyValuePair<CellAddress, ReportCell>(address, cells[address])).ToArray();
            var cell = cells.GetValueOrDefault(range.First, new(null, CellStyle.Default));
            cells[range.First] = cell with
            {
                RowSpan = range.Last.Row - range.First.Row + 1,
                ColumnSpan = range.Last.Column - range.First.Column + 1,
                Style = styles.Intern(cell.Style with { Border = null }),
                MergedBorders = ReadMergedBorders(entries, range, styles),
            };
            foreach (var address in entries.Select(entry => entry.Key).Where(address => address != range.First))
            {
                cells.Remove(address);
            }
        }

        return cells;
    }
}
