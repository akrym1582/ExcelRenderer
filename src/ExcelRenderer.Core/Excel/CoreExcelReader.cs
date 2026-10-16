using ClosedXML.Excel;
using ClosedXML.Excel.Drawings;
using ClosedXML.Graphics;
using ExcelRenderer.Core.Fonts;
using ExcelRenderer.Core.Input;
using ExcelRenderer.Core.Model;
using ExcelRenderer.Core.Rendering;

namespace ExcelRenderer.Core.Excel;

/// <summary>Excel ブックのワークシート、セル、印刷設定、画像をレンダリング用モデルとして読み込みます。</summary>
internal partial class CoreExcelReader
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

    /// <summary>Gets a value indicating whether to capture original run metadata.</summary>
    protected virtual bool CaptureRichText => false;

    /// <summary>Gets a value indicating whether conditional rule ranges are needed.</summary>
    protected virtual bool CaptureConditionalFormatting => false;

    /// <summary>Reads workbook models with optional selected-sheet body projection.</summary>
    /// <param name="source">The source used by this operation.</param>
    /// <param name="diagnostics">The diagnostics used by this operation.</param>
    /// <param name="selectedSheets">The selectedSheets used by this operation.</param>
    /// <param name="cancellationToken">The reader cancellation token.</param>
    /// <param name="formulaMode">The internal formula display policy.</param>
    /// <param name="options">The engine selection and formula display policy.</param>
    /// <returns>The planned or generated result.</returns>
    internal ReportDocument Read(PreparedWorkbook source, DiagnosticCollector? diagnostics, IReadOnlyList<string>? selectedSheets = null, CancellationToken cancellationToken = default, FormulaDisplayMode formulaMode = FormulaDisplayMode.Legacy, ExcelReadOptions? options = null)
    {
        options ??= new ExcelReadOptions { FormulaDisplayMode = (Core.FormulaDisplayMode)formulaMode };
        options.Validate();
        if (options.Backend != ReaderBackend.ClosedXml && options.FormulaDisplayMode == Core.FormulaDisplayMode.CachedValue)
        {
            var candidate = ReadOpenXml(source, diagnostics, selectedSheets, cancellationToken, options, out var reasons);
            if (candidate is not null)
            {
                return candidate;
            }

            options.BackendSelected?.Invoke(new(options.Backend, ReaderBackend.ClosedXml, reasons));
            return Read(source, diagnostics, selectedSheets, cancellationToken, FormulaDisplayMode.CachedValue, options with { Backend = ReaderBackend.ClosedXml, BackendSelected = null, InspectCandidate = null });
        }

        options.BackendSelected?.Invoke(new(options.Backend, ReaderBackend.ClosedXml, Array.AsReadOnly(new[] { options.Backend == ReaderBackend.Auto ? "LegacyFormulaMode" : "ExplicitClosedXml" })));
        using var workbookStream = source.OpenRead();
        using var cancelRead = new CancellationReadStream(workbookStream, cancellationToken);
        var graphics = (fontMetrics as IClosedXmlFontMetrics)?.GraphicEngine;
        using var workbook = ConversionMetrics.Measure("closedXml", () => graphics is null
            ? new XLWorkbook(cancelRead)
            : new XLWorkbook(cancelRead, new LoadOptions { GraphicEngine = graphics }));
        ConversionMetrics.Report("coreReaderWorkbook", 1);
        return ReadClosedXmlWorkbook(source, workbook, diagnostics, selectedSheets, cancellationToken, formulaMode);
    }

    /// <summary>Adapts a caller-owned legacy workbook while caches are read from immutable original input.</summary>
    /// <param name="source">Original validated bytes.</param>
    /// <param name="workbook">Caller-owned loaded legacy workbook.</param>
    /// <param name="diagnostics">Source diagnostics.</param>
    /// <param name="selectedSheets">Optional selected bodies.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <param name="formulaMode">Stored display policy.</param>
    /// <returns>The neutral rendering document.</returns>
    internal ReportDocument ReadClosedXmlWorkbook(PreparedWorkbook source, XLWorkbook workbook, DiagnosticCollector? diagnostics, IReadOnlyList<string>? selectedSheets, CancellationToken cancellationToken, FormulaDisplayMode formulaMode)
    {
        using var pictureSource = source.OpenRead();
        using var pictureMetadataStream = new CancellationReadStream(pictureSource, cancellationToken);
        using var metadataSource = source.OpenRead();
        using var metadataStream = new CancellationReadStream(metadataSource, cancellationToken);
        var graphics = (fontMetrics as IClosedXmlFontMetrics)?.GraphicEngine;
        var pictureMetadata = DrawingMLReader.ReadPictureMetadata(pictureMetadataStream);
        var pageSetups = WorkbookLayoutMetadataReader.ReadPageSetups(metadataStream);
        var metadata = new CoreWorkbookMetadata(pictureMetadata, pageSetups) { FormulaMode = formulaMode };
        if (formulaMode != FormulaDisplayMode.Legacy)
        {
            using var formulaStream = source.OpenRead();
            metadata.FormulaCaches = FormulaCacheReader.Read(formulaStream, workbook, cancellationToken);
        }

        PrepareWorkbookMetadata(source, workbook.Worksheets.Select(sheet => sheet.Name).ToArray(), metadata, selectedSheets);
        if (selectedSheets is not null)
        {
            foreach (var name in selectedSheets)
            {
                if (string.IsNullOrEmpty(name) || (!workbook.Worksheets.Any(sheet => sheet.Name == name) && !metadata.AdditionalSheets.Any(sheet => sheet.Name == name)))
                {
                    throw new ArgumentException($"Worksheet was not found: {name}", nameof(selectedSheets));
                }
            }
        }

        var styles = new StylePool();
        using var session = new ReaderSession(workbook.Use1904DateSystem, graphics);
        metadata.ReaderSession = session;
        return ConversionMetrics.Measure("model", () => new ReportDocument(workbook.Worksheets
            .Select((sheet, index) => (Sheet: sheet, Index: index + 1))
            .Where(entry => metadata.IncludeUnselectedSheetGeometry || selectedSheets is null || selectedSheets.Contains(entry.Sheet.Name, StringComparer.Ordinal))
            .Select(entry => ReadSheet(
                entry.Sheet,
                new CoreSheetReadContext(
                    metadata,
                    metadata.SourceSheetIndices.GetValueOrDefault(entry.Sheet.Name, entry.Index),
                    selectedSheets is null || selectedSheets.Contains(entry.Sheet.Name, StringComparer.Ordinal)),
                diagnostics,
                styles,
                graphics,
                cancellationToken))
            .Concat(metadata.AdditionalSheets.Where(sheet => selectedSheets is null || selectedSheets.Contains(sheet.Name, StringComparer.Ordinal)))
            .OrderBy(sheet => sheet.SourceSheetIndex).ToArray()));
    }

    /// <summary>Adds product metadata after the single workbook load and basic XML reads.</summary>
    /// <param name="source">The prepared source with independent cursors.</param>
    /// <param name="sheetNames">Original worksheet names.</param>
    /// <param name="metadata">The basic metadata to enrich.</param>
    /// <param name="selectedSheets">The requested sheet names.</param>
    protected virtual void PrepareWorkbookMetadata(PreparedWorkbook source, IReadOnlyList<string> sheetNames, CoreWorkbookMetadata metadata, IReadOnlyList<string>? selectedSheets)
    {
    }

    /// <summary>Determines whether an existing cell belongs in the body model.</summary>
    /// <param name="cell">The source cell.</param>
    /// <param name="context">The sheet read context.</param>
    /// <returns>Whether to materialize the cell.</returns>
    protected virtual bool ShouldReadCell(CellAddress cell, CoreSheetReadContext context) => true;

    /// <summary>Restores source-independent product displays before cache diagnostics.</summary>
    /// <param name="address">Original cell address.</param>
    /// <param name="context">Original sheet context.</param>
    /// <returns>A display override, or null to use the stored value.</returns>
    protected virtual string? DisplayOverride(CellAddress address, CoreSheetReadContext context) => null;

    /// <summary>Completes a cell from neutral original run data.</summary>
    /// <param name="source">Optional original rich text.</param>
    /// <param name="cell">The neutral cell.</param>
    /// <param name="context">Original sheet context.</param>
    /// <returns>The product cell.</returns>
    protected virtual ReportCell CompleteCell(SourceCellText? source, ReportCell cell, CoreSheetReadContext context) => cell;

    /// <summary>Applies product text policy without altering the source value.</summary>
    /// <param name="text">The formatted text.</param>
    /// <returns>The normalized text.</returns>
    protected virtual string NormalizeDisplayText(string text) => DisplayText.WithoutVariationSelectors(text);

    /// <summary>Attaches typed product metadata after basic materialization.</summary>
    /// <param name="sheetName">The original source sheet name.</param>
    /// <param name="sheet">The basic model.</param>
    /// <param name="context">The sheet context.</param>
    /// <returns>The completed model.</returns>
    protected virtual ReportSheet CompleteSheet(string sheetName, ReportSheet sheet, CoreSheetReadContext context) => sheet;

    private static void AddFormulaDiagnostic(IXLCell cell, CoreWorkbookMetadata metadata, string code, string message)
    {
        if (!metadata.CellDiagnostics.TryGetValue(cell.Worksheet.Name, out var diagnostics))
        {
            diagnostics = [];
            metadata.CellDiagnostics[cell.Worksheet.Name] = diagnostics;
        }

        diagnostics.Add(new(code, DiagnosticSeverity.Warning, DiagnosticStage.Read, message, cell.Worksheet.Name, cell.Address.ToStringRelative()));
    }

    private static string ReadDisplay(IXLCell cell, ReaderSession session)
    {
        if (!cell.HasFormula || cell.FormulaA1.IndexOf("HYPERLINK", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return cell.GetFormattedString();
        }

        return session.Format(cell, cell.CachedValue);
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

    private static SourceCellText? ReadLegacyCellText(IXLCell source)
    {
        if (source.HasFormula || !source.HasRichText)
        {
            return null;
        }

        var runs = new List<SourceTextRun>();
        var unsupported = source.GetRichText().HasPhonetics;
        foreach (var run in source.GetRichText())
        {
            unsupported |= run.Strikethrough || run.VerticalAlignment != XLFontVerticalTextAlignmentValues.Baseline ||
                run.Underline is not (XLFontUnderlineValues.None or XLFontUnderlineValues.Single);
            runs.Add(new(run.Text, new FontStyle(run.FontName, run.FontSize, run.Underline == XLFontUnderlineValues.Single, ExcelStyleConverter.ToColor(run.FontColor, source.Worksheet.Workbook.Theme))
            {
                Bold = run.Bold,
                Italic = run.Italic,
            }));
        }

        return new(runs.AsReadOnly(), unsupported);
    }

    private string ReadDisplayText(IXLCell cell, CoreSheetReadContext context)
    {
        if (DisplayOverride(new(cell.Address.RowNumber, cell.Address.ColumnNumber), context) is { } original)
        {
            return original;
        }

        var metadata = context.Metadata;
        if (!cell.HasFormula || metadata.FormulaMode == FormulaDisplayMode.Legacy)
        {
            var text = ReadDisplay(cell, metadata.ReaderSession!);
            if (cell.HasFormula && cell.CachedValue.IsError)
            {
                AddFormulaDiagnostic(cell, metadata, "FormulaResultError", "The selected formula display value is an Excel error.");
            }

            return text;
        }

        var address = new CellAddress(cell.Address.RowNumber, cell.Address.ColumnNumber);
        var cache = metadata.FormulaCaches.GetValueOrDefault(cell.Worksheet.Name)?.GetValueOrDefault(address);
        if (cache?.HasValue != true)
        {
            AddFormulaDiagnostic(cell, metadata, "FormulaCachedValueMissing", "The formula has no saved result; an empty display is used.");
            return string.Empty;
        }

        if (cache.Value.IsError)
        {
            AddFormulaDiagnostic(cell, metadata, "FormulaResultError", "The selected formula display value is an Excel error.");
        }

        return metadata.ReaderSession!.Format(cell, cache.Value);
    }

    private ReportSheet ReadSheet(
        IXLWorksheet worksheet,
        CoreSheetReadContext context,
        DiagnosticCollector? diagnostics,
        StylePool styles,
        IXLGraphicEngine? graphics,
        CancellationToken cancellationToken)
    {
        context = context with
        {
            SheetName = worksheet.Name,
            ConditionalFormattingRanges = CaptureConditionalFormatting ? worksheet.ConditionalFormats.Select(rule => (IReadOnlyList<CellRange>)rule.Ranges.Select(range => new CellRange(
                new(range.RangeAddress.FirstAddress.RowNumber, range.RangeAddress.FirstAddress.ColumnNumber),
                new(range.RangeAddress.LastAddress.RowNumber, range.RangeAddress.LastAddress.ColumnNumber))).ToArray()).ToArray() : [],
        };
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
                if (!ShouldReadCell(new(cell.Address.RowNumber, cell.Address.ColumnNumber), context))
                {
                    continue;
                }

                var address = new CellAddress(cell.Address.RowNumber, cell.Address.ColumnNumber);
                var model = new ReportCell(
                    NormalizeDisplayText(ReadDisplayText(cell, context)),
                    styles.Intern(context.Metadata.ReaderSession!.ConvertStyle(cell, source => NormalizeCellStyle(ExcelStyleConverter.ConvertOriginal(source)))),
                    Formula: cell.HasFormula ? "=" + cell.FormulaA1 : null);
                cells[address] = CompleteCell(CaptureRichText ? ReadLegacyCellText(cell) : null, model, context);
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
            cells = WorkbookModelBuilder.ApplyMergedSpans(cells, mergedRanges, styles);
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
        return CompleteSheet(worksheet.Name, sheet, context);

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
        if (metadata is null && (fontMetrics as IClosedXmlFontMetrics)?.DefaultColumnWidth(worksheet.ColumnWidth) is { } legacyWidth)
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
