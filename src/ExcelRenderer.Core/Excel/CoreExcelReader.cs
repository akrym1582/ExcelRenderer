using ClosedXML.Excel;
using ClosedXML.Excel.Drawings;
using ClosedXML.Graphics;
using ExcelRenderer.Core.Fonts;
using ExcelRenderer.Core.Input;
using ExcelRenderer.Core.Model;
using ExcelRenderer.Core.Rendering;

namespace ExcelRenderer.Core.Excel;

/// <summary>Excel ブックのワークシート、セル、印刷設定、画像をレンダリング用モデルとして読み込みます。</summary>
internal class CoreExcelReader
{
    private readonly ICoreFontMetrics fontMetrics;

    /// <summary>Initializes a new instance of the <see cref="CoreExcelReader"/> class.</summary>
    /// <param name="fontManager">The single conversion font.</param>
    internal CoreExcelReader(SingleFontContext fontManager)
        : this(new SingleFontMetrics(fontManager))
    {
    }

    /// <summary>Initializes a new instance of the <see cref="CoreExcelReader"/> class.</summary>
    /// <param name="fontMetrics">The product font metrics.</param>
    internal CoreExcelReader(ICoreFontMetrics fontMetrics) => this.fontMetrics = fontMetrics;

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
        var graphics = fontMetrics.GraphicEngine;
        using var workbook = ConversionMetrics.Measure("closedXml", () => graphics is null
            ? new XLWorkbook(cancelRead)
            : new XLWorkbook(cancelRead, new LoadOptions { GraphicEngine = graphics }));
        ConversionMetrics.Report("coreReaderWorkbook", 1);
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
        var metadata = new CoreWorkbookMetadata(pictureMetadata, pageSetups);
        PrepareWorkbookMetadata(source, workbook, metadata, selectedSheets);
        var styles = new StylePool();
        return ConversionMetrics.Measure("model", () => new ReportDocument(workbook.Worksheets
            .Select((sheet, index) => (Sheet: sheet, Index: index + 1))
            .Where(entry => metadata.IncludeUnselectedSheetGeometry || selectedSheets is null || selectedSheets.Contains(entry.Sheet.Name, StringComparer.Ordinal))
            .Select(entry => ReadSheet(
                entry.Sheet,
                new CoreSheetReadContext(
                    metadata,
                    entry.Index,
                    selectedSheets is null || selectedSheets.Contains(entry.Sheet.Name, StringComparer.Ordinal)),
                diagnostics,
                styles,
                graphics,
                cancellationToken)).ToArray()));
    }

    /// <summary>Adds product metadata after the single workbook load and basic XML reads.</summary>
    /// <param name="source">The prepared source with independent cursors.</param>
    /// <param name="workbook">The loaded workbook.</param>
    /// <param name="metadata">The basic metadata to enrich.</param>
    /// <param name="selectedSheets">The requested sheet names.</param>
    protected virtual void PrepareWorkbookMetadata(PreparedWorkbook source, XLWorkbook workbook, CoreWorkbookMetadata metadata, IReadOnlyList<string>? selectedSheets)
    {
    }

    /// <summary>Determines whether an existing cell belongs in the body model.</summary>
    /// <param name="cell">The source cell.</param>
    /// <param name="context">The sheet read context.</param>
    /// <returns>Whether to materialize the cell.</returns>
    protected virtual bool ShouldReadCell(IXLCell cell, CoreSheetReadContext context) => true;

    /// <summary>Reads display values before product normalization.</summary>
    /// <param name="cell">The source cell.</param>
    /// <param name="context">The sheet read context.</param>
    /// <returns>The original formatted display.</returns>
    protected virtual string ReadDisplayText(IXLCell cell, CoreSheetReadContext context) => ReadDisplay(cell, fontMetrics.GraphicEngine);

    /// <summary>Converts source styles before interning.</summary>
    /// <param name="cell">The source cell.</param>
    /// <returns>The product style.</returns>
    protected virtual CellStyle ConvertCellStyle(IXLCell cell) => ExcelStyleConverter.Convert(cell);

    /// <summary>Applies product text policy without altering the source value.</summary>
    /// <param name="text">The formatted text.</param>
    /// <returns>The normalized text.</returns>
    protected virtual string NormalizeDisplayText(string text) => DisplayText.WithoutVariationSelectors(text);

    /// <summary>Attaches typed product metadata after basic materialization.</summary>
    /// <param name="worksheet">The source sheet.</param>
    /// <param name="sheet">The basic model.</param>
    /// <param name="context">The sheet context.</param>
    /// <returns>The completed model.</returns>
    protected virtual ReportSheet CompleteSheet(IXLWorksheet worksheet, ReportSheet sheet, CoreSheetReadContext context) => sheet;

    private static string ReadDisplay(IXLCell cell, IXLGraphicEngine? graphics)
    {
        if (!cell.HasFormula || cell.FormulaA1.IndexOf("HYPERLINK", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return cell.GetFormattedString();
        }

        using var scratch = graphics is null ? new XLWorkbook() : new XLWorkbook(new LoadOptions { GraphicEngine = graphics });
        var cached = scratch.AddWorksheet("cached").Cell(1, 1);
        cached.Value = cell.CachedValue;
        cached.Style = cell.Style;
        return cached.GetFormattedString();
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

    private static ReportImage ReadImage(
        IXLPicture picture,
        DrawingPictureMetadata? metadata,
        int zIndex,
        bool includeBytes)
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
            includeBytes ? picture.ImageStream.ToArray() : [],
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

    private ReportSheet ReadSheet(
        IXLWorksheet worksheet,
        CoreSheetReadContext context,
        DiagnosticCollector? diagnostics,
        StylePool styles,
        IXLGraphicEngine? graphics,
        CancellationToken cancellationToken)
    {
        var pictureMetadata = context.Metadata.Pictures.GetValueOrDefault(
            worksheet.Name,
            new Dictionary<string, DrawingPictureMetadata>(StringComparer.Ordinal));
        var pageSetupMetadata = context.Metadata.PageSetups.GetValueOrDefault(worksheet.Name);
        var cells = new Dictionary<CellAddress, ReportCell>();
        var columns = new Dictionary<int, ColumnDefinition>();
        var rows = new Dictionary<int, RowDefinition>();
        var usedRange = worksheet.RangeUsed(XLCellsUsedOptions.All);
        if (usedRange is not null)
        {
            foreach (var cell in context.IncludeBody ? usedRange.CellsUsed(XLCellsUsedOptions.All) : Enumerable.Empty<IXLCell>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!ShouldReadCell(cell, context))
                {
                    continue;
                }

                var address = new CellAddress(cell.Address.RowNumber, cell.Address.ColumnNumber);
                cells[address] = new(
                    NormalizeDisplayText(ReadDisplayText(cell, context)),
                    styles.Intern(ConvertCellStyle(cell)),
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
                    worksheet.Name);
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
                            worksheet.Name);
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
        if (context.IncludeBody)
        {
            cells = ApplyMergedSpans(cells, mergedRanges, styles);
        }

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
                        worksheet.Name);
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
            index,
            context.IncludeBody)).ToArray();
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
                    worksheet.Name);
            }

            if (!rows.ContainsKey(image.Anchor.Row))
            {
                var source = worksheet.Row(image.Anchor.Row);
                rows[image.Anchor.Row] = new(source.Height, source.IsHidden);
            }
        }

        var geometryRanges = printAreas.Concat(mergedRanges)
            .Concat(context.Metadata.AdditionalGeometry.GetValueOrDefault(worksheet.Name, []))
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
                        worksheet.Name);
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
            context.IncludeBody ? images : [],
            ReadHeaderFooter(worksheet))
        {
            DefaultColumnWidth = GetDefaultColumnWidth(
                worksheet,
                pageSetupMetadata,
                diagnostics),
            DefaultRowHeight = pageSetupMetadata?.DefaultRowHeight ?? worksheet.RowHeight,
            PrintAreas = printAreas,
            SourceSheetIndex = context.SourceSheetIndex,
        };
        return CompleteSheet(worksheet, sheet, context);

        static IEnumerable<CellRange> GetAnchorRanges(CellAddress fallback, DrawingAnchor? anchor)
        {
            var from = anchor?.From ?? fallback;
            var to = anchor?.To ?? from;
            yield return new(
                new(Math.Min(from.Row, to.Row), Math.Min(from.Column, to.Column)),
                new(Math.Max(from.Row, to.Row), Math.Max(from.Column, to.Column)));
        }
    }

    private ColumnDefinition ReadColumnDefinition(
        int column,
        IXLColumn source,
        SheetPageSetupMetadata? metadata,
        DiagnosticCollector? diagnostics,
        string sheetName)
    {
        var raw = metadata?.Columns.LastOrDefault(definition =>
            column >= definition.First && column <= definition.Last);
        var maximumDigitWidth = ResolveMaximumDigitWidth(
            metadata,
            diagnostics,
            sheetName);
        var width = raw?.Width ?? metadata?.DefaultColumnWidth ??
            ColumnWidthCalculator.FromBaseColumnWidth(metadata?.BaseColumnWidth ?? 8, maximumDigitWidth);
        return new(ColumnWidthCalculator.ToPoints(width, maximumDigitWidth), raw?.Hidden ?? source.IsHidden);
    }

    private double GetDefaultColumnWidth(
        IXLWorksheet worksheet,
        SheetPageSetupMetadata? metadata,
        DiagnosticCollector? diagnostics)
    {
        if (metadata is null && fontMetrics.DefaultColumnWidth(worksheet.ColumnWidth) is { } legacyWidth)
        {
            return legacyWidth;
        }

        var maximumDigitWidth = ResolveMaximumDigitWidth(
            metadata,
            diagnostics,
            worksheet.Name);
        var width = metadata?.DefaultColumnWidth ??
            ColumnWidthCalculator.FromBaseColumnWidth(metadata?.BaseColumnWidth ?? 8, maximumDigitWidth);
        return ColumnWidthCalculator.ToPoints(width, maximumDigitWidth);
    }

    private double ResolveMaximumDigitWidth(
        SheetPageSetupMetadata? metadata,
        DiagnosticCollector? diagnostics,
        string sheetName)
    {
        return fontMetrics.MaximumDigitWidth(metadata?.NormalFont, sheetName);
    }

    private HeaderFooter? ReadHeaderFooter(IXLWorksheet worksheet)
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

    private HeaderFooterSection ReadHeaderFooterSection(
        IXLHeaderFooter headerFooter,
        XLHFOccurrence occurrence = XLHFOccurrence.OddPages) =>
        new(
            NormalizeDisplayText(headerFooter.Left.GetText(occurrence)),
            NormalizeDisplayText(headerFooter.Center.GetText(occurrence)),
            NormalizeDisplayText(headerFooter.Right.GetText(occurrence)));
}
