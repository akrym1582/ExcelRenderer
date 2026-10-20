using System.Globalization;
using ExcelRenderer.Core.Input;
using ExcelRenderer.Core.Model;
using ExcelRenderer.Core.Rendering;
using ExcelRenderer.OpenXml.Reader;
using ExcelRenderer.OpenXml.Reader.Model.Cells;
using ExcelRenderer.OpenXml.Reader.Model.Drawing;
using ExcelRenderer.OpenXml.Reader.Model.Styles;
using ExcelRenderer.OpenXml.Reader.Model.Workbook;
using static ExcelRenderer.Core.Excel.OpenXmlStyleConverter;
using CellAddress = ExcelRenderer.Core.Model.CellAddress;
using CellRange = ExcelRenderer.Core.Model.CellRange;
using PreparedWorkbook = ExcelRenderer.OpenXml.Reader.Input.PreparedWorkbook;

namespace ExcelRenderer.Core.Excel;

/// <summary>Connects streaming source snapshots to the shared rendering model.</summary>
internal partial class CoreExcelReader
{
    /// <summary>Gets a value indicating whether product selection defers source diagnostics.</summary>
    protected virtual bool DeferOpenXmlDiagnostics => false;

    /// <summary>Applies the product's font and rotation policy.</summary>
    /// <param name="style">Original source appearance.</param>
    /// <returns>The product appearance.</returns>
    protected virtual CellStyle NormalizeCellStyle(CellStyle style) => style with
    {
        Font = style.Font with { Family = Fonts.SingleFontContext.Family, Bold = false, Italic = false },
        TextRotation = 0,
        TopToBottom = false,
    };

    /// <summary>Creates an optional value substitution for this read before cell enumeration starts.</summary>
    /// <param name="reader">The opened source; snapshot construction must finish before returning.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>A read-local substitution, or null to retain saved cells.</returns>
    protected virtual Func<CellRecord, CellRecord>? CreateCellValueResolver(IWorkbookDataReader reader, CancellationToken cancellationToken) => null;

    /// <summary>Completes product-specific cell appearances before shared merged-border aggregation.</summary>
    /// <param name="info">Immutable source sheet geometry.</param>
    /// <param name="cells">Read-local cells.</param>
    /// <param name="regions">Existing finite rendering regions.</param>
    /// <param name="blankStyle">Resolves inherited ordinary styles for missing cells.</param>
    /// <param name="styles">Read-local style interning.</param>
    /// <param name="token">Cooperative cancellation.</param>
    protected virtual void CompleteCellAppearances(WorksheetInfo info, Dictionary<CellAddress, ReportCell> cells, IReadOnlyList<CellRange> regions, Func<CellAddress, CellStyle> blankStyle, StylePool styles, CancellationToken token)
    {
    }

    private static SourceCellText? ReadOpenXmlCellText(CellRecord source, CellStyleInfo style)
    {
        if (source.Formula is not null || source.RichTextRuns.Count == 0)
        {
            return null;
        }

        var unsupported = source.HasPhonetic;
        var runs = new List<SourceTextRun>();
        foreach (var run in source.RichTextRuns)
        {
            var font = style.FontProperties.WithOverrides(run.FontProperties);
            unsupported |= font.Strikethrough == true || font.VerticalAlignment is not null and not "baseline" || font.Underline is not null and not "none" and not "single";
            runs.Add(new(run.Text, OpenXmlStyleConverter.Font(font) with { Underline = font.Underline == "single" }));
        }

        return new(runs.AsReadOnly(), unsupported);
    }

    private static IEnumerable<(string Code, string Message)> CompatibilityIssues(CellRecord cell, CellStyleInfo style)
    {
        if (cell.Value.Type == CellValueType.Number && style.NumberFormatId == 14 && style.NumberFormatCode == "mm-dd-yy")
        {
            yield return ("RegionalNumberFormat", "Built-in date format 14 uses its saved OOXML display; the compatibility reader may localize this format differently.");
        }

        if (cell.Value.Type == CellValueType.IsoDate)
        {
            yield return ("IsoDateDisplay", "The saved ISO date text is preserved without compatibility date formatting.");
        }

        if (Unresolved(style.Font) || Unresolved(style.Fill) || Unresolved(style.Border) || cell.RichTextRuns.Any(run => run.FontProperties?.Color is { } color && Unresolved(color)))
        {
            yield return ("UnresolvedColor", "A selected source color could not be resolved to the compatibility appearance.");
        }

        static bool Unresolved(ElementInfo element) =>
            ((element.Name is "color" or "fgColor" or "bgColor") && element.Attributes.Count > 0 &&
                Attribute(element, "resolvedRgb") is null && Attribute(element, "rgb") is null &&
                Attribute(element, "auto") is not "1" and not "true" && Attribute(element, "indexed") is not "64" and not "65") || element.Children.Any(Unresolved);
    }

    private static CellRange ToRange(OpenXml.Reader.Model.Cells.CellRange range) => new(new(range.First.Row, range.First.Column), new(range.Last.Row, range.Last.Column));

    private static bool IsManualBreak(ElementInfo item) => Attribute(item, "man") switch
    {
        "1" or "true" => true,
        null or "0" or "false" => false,
        _ => throw new InvalidDataException("Invalid boolean attribute."),
    };

    private static IReadOnlyList<int> ManualBreaks(IEnumerable<ElementInfo> items) => items.Where(IsManualBreak)
        .Select(item => (int)Number(item, "id", 0)).Where(id => id > 0).Distinct().OrderBy(id => id).ToArray();

    private static double ValidHeight(double? height, double fallback) =>
        height is { } value && !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0 ? value : fallback;

    private static PageSettings OpenXmlPageSettings(WorksheetInfo info)
    {
        var fit = Boolean(info.PageSetupProperties, "fitToPage");
        var (width, height) = PaperSizeConverter.ToPoints((int)Number(info.PageSetup, "paperSize", 1));
        if (Attribute(info.PageSetup, "orientation") == "landscape")
        {
            (width, height) = (height, width);
        }

        var titles = info.PrintReferences.Where(reference => reference.Name == "_xlnm.Print_Titles").SelectMany(reference => reference.Ranges).ToArray();
        var rowTitle = titles.FirstOrDefault(range => range.Kind == "Rows");
        var columnTitle = titles.FirstOrDefault(range => range.Kind == "Columns");
        return new(
            width,
            height,
            Number(info.PageMargins, "left", 0.75) * 72,
            Number(info.PageMargins, "top", 0.75) * 72,
            Number(info.PageMargins, "right", 0.75) * 72,
            Number(info.PageMargins, "bottom", 0.5) * 72,
            fit ? null : Number(info.PageSetup, "scale", 100) / 100,
            fit ? (int)Number(info.PageSetup, "fitToWidth", 1) : null,
            fit ? (int)Number(info.PageSetup, "fitToHeight", 1) : null,
            rowTitle is null ? null : new(rowTitle.FirstRow, rowTitle.LastRow),
            columnTitle is null ? null : new(columnTitle.FirstColumn, columnTitle.LastColumn))
        {
            ScaleMode = fit ? PrintScaleMode.FitToPages : PrintScaleMode.Explicit,
            ManualRowBreaks = ManualBreaks(info.RowBreaks),
            ManualColumnBreaks = ManualBreaks(info.ColumnBreaks),
            PageOrder = Attribute(info.PageSetup, "pageOrder") == "overThenDown" ? PrintPageOrder.OverThenDown : PrintPageOrder.DownThenOver,
            HorizontalCentered = Boolean(info.PrintOptions, "horizontalCentered"),
            VerticalCentered = Boolean(info.PrintOptions, "verticalCentered"),
        };
    }

    private static ReportImage OpenXmlImage(ImageInfo image, byte[] bytes)
    {
        var anchor = image.Anchor;
        var from = Child(anchor, "from");
        var to = Child(anchor, "to");
        var position = Child(anchor, "pos");
        var extent = Child(anchor, "ext");
        var picture = Child(anchor, "pic");
        var transform = Child(Child(picture, "spPr"), "xfrm");
        var size = Child(transform, "ext");
        var drawing = new DrawingAnchor(
            anchor.Name == "absoluteAnchor" ? DrawingAnchorKind.Absolute : anchor.Name == "twoCellAnchor" ? DrawingAnchorKind.TwoCell : DrawingAnchorKind.OneCell,
            Marker(from),
            Coordinate(from, "colOff"),
            Coordinate(from, "rowOff"),
            Marker(to),
            Coordinate(to, "colOff"),
            Coordinate(to, "rowOff"),
            Number(position, "x", 0) / 12700,
            Number(position, "y", 0) / 12700,
            Number(extent, "cx", 0) / 12700,
            Number(extent, "cy", 0) / 12700,
            Attribute(anchor, "editAs"));
        return new(
            drawing.From ?? new(1, 1),
            drawing.Kind == DrawingAnchorKind.Absolute ? drawing.PositionX : drawing.FromOffsetX,
            drawing.Kind == DrawingAnchorKind.Absolute ? drawing.PositionY : drawing.FromOffsetY,
            drawing.ExtentWidth > 0 ? drawing.ExtentWidth : Math.Round(Number(size, "cx", 0) / 9525) * 0.75,
            drawing.ExtentHeight > 0 ? drawing.ExtentHeight : Math.Round(Number(size, "cy", 0) / 9525) * 0.75,
            bytes,
            image.ZIndex,
            image.Name,
            image.ContentType,
            null) { DrawingAnchor = drawing, SourceImage = image };

        static CellAddress? Marker(ElementInfo? element) => element is null ? null : new((int)Coordinate(element, "row", false) + 1, (int)Coordinate(element, "col", false) + 1);
        static double Coordinate(ElementInfo? element, string name, bool emu = true) => Child(element, name)?.Text is { Length: > 0 } text ? double.Parse(text, CultureInfo.InvariantCulture) / (emu ? 12700 : 1) : 0;
    }

    private HeaderFooter? OpenXmlHeaderFooter(ElementInfo? source)
    {
        if (source is null || source.Children.All(child => child.Text.Length == 0))
        {
            return null;
        }

        return new(
            Section("oddHeader"),
            Section("oddFooter"),
            Boolean(source, "differentFirst") ? Section("firstHeader") : null,
            Boolean(source, "differentFirst") ? Section("firstFooter") : null,
            Boolean(source, "differentOddEven") ? Section("evenHeader") : null,
            Boolean(source, "differentOddEven") ? Section("evenFooter") : null);

        HeaderFooterSection Section(string name)
        {
            var text = Child(source, name)?.Text ?? string.Empty;
            var parts = new System.Text.StringBuilder[] { new(), new(), new() };
            var selected = 1;
            var quoted = false;
            for (var index = 0; index < text.Length; index++)
            {
                if (text[index] == '"')
                {
                    quoted = !quoted;
                }

                if (!quoted && text[index] == '&' && index + 1 < text.Length)
                {
                    var marker = text[index + 1];
                    if (marker is 'L' or 'C' or 'R')
                    {
                        selected = marker == 'L' ? 0 : marker == 'C' ? 1 : 2;
                        index++;
                        continue;
                    }

                    if (marker == '&')
                    {
                        parts[selected].Append("&&");
                        index++;
                        continue;
                    }
                }

                parts[selected].Append(text[index]);
            }

            return new(NormalizeDisplayText(parts[0].ToString()), NormalizeDisplayText(parts[1].ToString()), NormalizeDisplayText(parts[2].ToString()));
        }
    }

    private ReportDocument ReadOpenXml(PreparedWorkbook source, DiagnosticCollector? diagnostics, IReadOnlyList<string>? selectedSheets, CancellationToken token, ExcelReadOptions options)
    {
        using var stream = source.OpenRead();
        using var reader = OpenXmlWorkbookReader.Open(stream, new() { SelectedSheetNames = selectedSheets, Culture = CultureInfo.CurrentCulture }, token);
        var resolveCellValue = CreateCellValueResolver(reader, token);
        ConversionMetrics.Report("openXmlReaderWorkbook", 1);
        var infos = reader.Sheets.Where(sheet => sheet.Kind == "Worksheet").Select(sheet => reader.ReadWorksheetInfo(sheet.Index, token)).ToArray();
        var metadata = new CoreWorkbookMetadata();
        foreach (var original in reader.Sheets)
        {
            metadata.SourceSheetIndices[original.Name] = original.Index;
        }

        PrepareWorkbookMetadata(source, infos.Select(info => info.Sheet.Name).ToArray(), metadata, selectedSheets);
        foreach (var name in selectedSheets ?? Array.Empty<string>())
        {
            if (!infos.Any(info => info.Sheet.Name == name) && !metadata.AdditionalSheets.Any(sheet => sheet.Name == name))
            {
                throw new ArgumentException($"Worksheet was not found: {name}", nameof(selectedSheets));
            }
        }

        var images = infos.Where(info => reader.SelectedWorksheetIndices.Contains(info.Sheet.Index)).ToDictionary(info => info.Sheet.Index, info => reader.ReadImages(info.Sheet.Index, token));
        var styles = new StylePool();
        var styleCache = new Dictionary<(int Index, CellValueType Type), CellStyle>();
        long styleConversions = 0;
        long styleHits = 0;
        var bytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var sheets = new List<ReportSheet>();
        foreach (var info in infos.Where(info => metadata.IncludeUnselectedSheetGeometry || reader.SelectedWorksheetIndices.Contains(info.Sheet.Index)))
        {
            token.ThrowIfCancellationRequested();
            var includeBody = reader.SelectedWorksheetIndices.Contains(info.Sheet.Index);
            var context = new CoreSheetReadContext(metadata, info.Sheet.Index, includeBody)
            {
                SheetName = info.Sheet.Name,
                ConditionalFormattingRanges = CaptureConditionalFormatting ? info.ConditionalFormattingRanges.Select(ranges => (IReadOnlyList<CellRange>)ranges.Select(ToRange).ToArray()).ToArray() : [],
            };
            var cells = new Dictionary<CellAddress, ReportCell>();
            var cellDiagnostics = new List<Rendering.ConversionDiagnostic>();
            metadata.CellDiagnostics[info.Sheet.Name] = cellDiagnostics;
            foreach (var reference in info.PrintReferences.Where(reference => reference.Ranges.Count == 0))
            {
                cellDiagnostics.Add(new("UnsupportedPrintReference", DiagnosticSeverity.Warning, DiagnosticStage.Read, "The saved print definition is outside the supported grammar: " + reference.RawText, info.Sheet.Name));
            }

            var paperSize = (int)Number(info.PageSetup, "paperSize", 1);
            if (!PaperSizeConverter.IsSupported(paperSize))
            {
                cellDiagnostics.Add(new("UnsupportedPaperSize", DiagnosticSeverity.Warning, DiagnosticStage.Read, "The saved paper size is unsupported; A4 dimensions are used.", info.Sheet.Name));
            }

            if (Attribute(info.PageSetup, "paperWidth") is not null || Attribute(info.PageSetup, "paperHeight") is not null)
            {
                cellDiagnostics.Add(new("UnsupportedPaperDimensions", DiagnosticSeverity.Warning, DiagnosticStage.Read, "Explicit paperWidth/paperHeight lengths are not interpreted; the paperSize dimensions are used.", info.Sheet.Name));
            }

            var pageSettings = OpenXmlPageSettings(info);
            diagnostics?.Add(new(
                "SavedPageBreaks",
                DiagnosticSeverity.Info,
                DiagnosticStage.Read,
                $"Manual row breaks: {pageSettings.ManualRowBreaks?.Count ?? 0}; manual column breaks: {pageSettings.ManualColumnBreaks?.Count ?? 0}; excluded automatic row breaks: {info.RowBreaks.Count(item => !IsManualBreak(item))}; excluded automatic column breaks: {info.ColumnBreaks.Count(item => !IsManualBreak(item))}; saved print scale percent: {Attribute(info.PageSetup, "scale") ?? "100"}.",
                info.Sheet.Name));

            var rowDefaults = info.Rows.Where(row => row.CustomFormat == true && row.StyleIndex is not null).ToDictionary(row => row.Index, row => row.StyleIndex!.Value);
            var columnDefaults = new int?[16385];
            foreach (var definition in info.Columns)
            {
                for (var column = definition.Min; column <= definition.Max; column++)
                {
                    columnDefaults[column] = definition.StyleIndex;
                }
            }

            var pendingLeaders = info.MergedRanges.Select(range => range.First).Distinct().OrderBy(address => address.Row).ThenBy(address => address.Column).ToArray();
            var leaderIndex = 0;
            foreach (var sourceCell in includeBody ? reader.EnumerateCells(info.Sheet.Index, token) : Enumerable.Empty<CellRecord>())
            {
                var cell = resolveCellValue is null ? sourceCell : resolveCellValue(sourceCell);
                while (leaderIndex < pendingLeaders.Length && (pendingLeaders[leaderIndex].Row < cell.Address.Row ||
                    (pendingLeaders[leaderIndex].Row == cell.Address.Row && pendingLeaders[leaderIndex].Column < cell.Address.Column)))
                {
                    AddMissingLeader(pendingLeaders[leaderIndex++]);
                }

                var isLeader = leaderIndex < pendingLeaders.Length && pendingLeaders[leaderIndex].Equals(cell.Address);
                leaderIndex += isLeader ? 1 : 0;
                var originalStyle = reader.ResolveStyle(cell);
                if (!styleCache.TryGetValue((cell.StyleIndex, cell.Value.Type), out var style))
                {
                    styleConversions++;
                    style = styles.Intern(NormalizeCellStyle(OpenXmlStyleConverter.Convert(originalStyle, cell.Value.Type)));
                    if (styleCache.Count < 8192)
                    {
                        styleCache.Add((cell.StyleIndex, cell.Value.Type), style);
                    }
                }
                else
                {
                    styleHits++;
                }

                if (cell.Value.Type == CellValueType.Blank && cell.Formula is null && cell.StyleIndex == 0 && style.Background is null && style.Border is null && !isLeader &&
                    !Paint(BlankStyle(rowDefaults.TryGetValue(cell.Address.Row, out var inheritedRow) ? inheritedRow : columnDefaults[cell.Address.Column] ?? 0)))
                {
                    continue;
                }

                foreach (var issue in CompatibilityIssues(cell, originalStyle))
                {
                    cellDiagnostics.Add(new(issue.Code, DiagnosticSeverity.Warning, DiagnosticStage.Read, issue.Message, info.Sheet.Name, cell.Address.ToString()));
                }

                var display = reader.FormatCell(cell);
                foreach (var diagnostic in display.Diagnostics)
                {
                    cellDiagnostics.Add(new(diagnostic.Code, DiagnosticSeverity.Warning, DiagnosticStage.Read, diagnostic.Message, info.Sheet.Name, cell.Address.ToString()));
                }

                var model = new ReportCell(NormalizeDisplayText(display.Text), style, Formula: cell.Formula?.RawFormula is { Length: > 0 } formula ? "=" + formula : null)
                {
                    FormulaSharedIndex = cell.Formula?.SharedIndex,
                    FormulaMasterAddress = cell.Formula?.MasterAddress is { } master ? new(master.Row, master.Column) : null,
                };
                cells.Add(new(cell.Address.Row, cell.Address.Column), CompleteCell(CaptureRichText ? ReadOpenXmlCellText(cell, originalStyle) : null, model, context));
            }

            var merged = info.MergedRanges.Select(ToRange).OrderBy(range => range.First.Row).ThenBy(range => range.First.Column).ThenBy(range => range.Last.Row).ThenBy(range => range.Last.Column).ToArray();
            if (includeBody)
            {
                while (leaderIndex < pendingLeaders.Length)
                {
                    AddMissingLeader(pendingLeaders[leaderIndex++]);
                }

                var regions = options.GetSelectedRange?.Invoke(info.Sheet.Name) is { } selected
                    ? new[] { new CellRange(new(selected.FirstRow, selected.FirstColumn), new(selected.LastRow, selected.LastColumn)) }
                    : info.PrintReferences.Where(reference => reference.Name == "_xlnm.Print_Area").SelectMany(reference => reference.Ranges)
                        .Select(range => new CellRange(new(range.FirstRow, range.FirstColumn), new(range.LastRow, range.LastColumn))).ToArray();
                if (regions.Length == 0 && cells.Count > 0)
                {
                    regions = [new(new(cells.Keys.Min(address => address.Row), cells.Keys.Min(address => address.Column)), new(cells.Keys.Max(address => address.Row), cells.Keys.Max(address => address.Column)))];
                }

                WorkbookModelBuilder.AddStyledBlanks(rowDefaults, columnDefaults, regions, cells, BlankStyle);
                CompleteCellAppearances(info, cells, regions, address => BlankStyle(rowDefaults.TryGetValue(address.Row, out var rowStyle) ? rowStyle : columnDefaults[address.Column] ?? 0), styles, token);
                WorkbookModelBuilder.ApplyMergedSpans(cells, merged, styles);
            }

            var normal = reader.Workbook.DefaultFontProperties;
            var mdw = fontMetrics.MaximumDigitWidth(reader.Workbook.DefaultFont.Children.Count == 0 ? null : new(normal.Name ?? "Calibri", normal.SizePt ?? 11, normal.Bold == true ? 700 : 400, normal.Italic == true), info.Sheet.Name);
            var defaultWidth = info.DefaultColumnWidthExcel ?? ColumnWidthCalculator.FromBaseColumnWidth(info.BaseColumnWidthCharacters, mdw);
            var columns = new Dictionary<int, ColumnDefinition>();
            foreach (var column in info.Columns)
            {
                for (var index = column.Min; index <= column.Max; index++)
                {
                    columns[index] = new(ColumnWidthCalculator.ToPoints(column.Width ?? defaultWidth, mdw), column.Hidden ?? false);
                }
            }

            var rows = info.Rows.ToDictionary(row => row.Index, row => new RowDefinition(ValidHeight(row.HeightPt, ValidHeight(info.DefaultRowHeightPt, 15)), row.Hidden ?? info.DefaultRowsHidden));
            var pictures = new List<ReportImage>();
            foreach (var image in images.GetValueOrDefault(info.Sheet.Index, Array.Empty<ImageInfo>()))
            {
                if (!bytes.TryGetValue(image.PartUri, out var content))
                {
                    using var imageStream = reader.OpenImageStream(image);
                    using var destination = new MemoryStream();
                    using var cancellableImage = new CancellationReadStream(imageStream, token);
                    cancellableImage.CopyTo(destination);
                    content = destination.ToArray();
                    bytes.Add(image.PartUri, content);
                }

                pictures.Add(OpenXmlImage(image, content));
            }

            var printAreas = info.PrintReferences.Where(reference => reference.Name == "_xlnm.Print_Area").SelectMany(reference => reference.Ranges)
                .Select(range => new CellRange(new(range.FirstRow, range.FirstColumn), new(range.LastRow, range.LastColumn))).ToArray();
            var sheet = new ReportSheet(info.Sheet.Name, cells, columns, rows, merged, pageSettings, printAreas.Length > 0 ? printAreas[0] : null, pictures, OpenXmlHeaderFooter(info.HeaderFooter))
            {
                DefaultColumnWidth = ColumnWidthCalculator.ToPoints(defaultWidth, mdw),
                DefaultRowHeight = ValidHeight(info.DefaultRowHeightPt, 15),
                DefaultRowsHidden = info.DefaultRowsHidden,
                PrintAreas = printAreas,
                SourceSheetIndex = info.Sheet.Index,
            };
            sheets.Add(CompleteSheet(info.Sheet.Name, sheet, context));

            void AddMissingLeader(OpenXml.Reader.Model.Cells.CellAddress address)
            {
                var index = rowDefaults.TryGetValue(address.Row, out var rowStyle) ? rowStyle : columnDefaults[address.Column] ?? 0;
                cells.Add(new(address.Row, address.Column), new(string.Empty, BlankStyle(index)));
            }

            static bool Paint(CellStyle style) => style.Background is not null || style.Border is not null;

            CellStyle BlankStyle(int index)
            {
                if (!styleCache.TryGetValue((index, CellValueType.Blank), out var style))
                {
                    style = styles.Intern(NormalizeCellStyle(OpenXmlStyleConverter.Convert(reader.ResolveStyle(index), CellValueType.Blank)));
                    if (styleCache.Count < 8192)
                    {
                        styleCache.Add((index, CellValueType.Blank), style);
                    }
                }

                return style;
            }
        }

        if (!DeferOpenXmlDiagnostics)
        {
            foreach (var diagnostic in metadata.CellDiagnostics.Values.SelectMany(items => items))
            {
                diagnostics?.Add(diagnostic);
            }
        }

        ConversionMetrics.Report("readerStyleConversions", styleConversions);
        ConversionMetrics.Report("readerStyleCacheHits", styleHits);
        ConversionMetrics.Report("readerStyleCacheEntries", styleCache.Count);
        var document = new ReportDocument(sheets.Concat(metadata.AdditionalSheets).OrderBy(sheet => sheet.SourceSheetIndex).ToArray());
        return document;
    }
}
