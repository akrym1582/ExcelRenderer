using System.Text;
using ExcelRenderer.Drawing;
using ExcelRenderer.Excel;
using ExcelRenderer.Fonts;
using ExcelRenderer.Layout;
using ExcelRenderer.Markdown;
using ExcelRenderer.Model;
using ExcelRenderer.PdfSharp;
using ExcelRenderer.Rendering;
using ExcelRenderer.SkiaSharp;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace ExcelRenderer;

/// <summary>Excel ブックをストリームから非同期に読み取り、指定された出力先へ変換します。</summary>
public static partial class ExcelConverter
{
    private static readonly SemaphoreSlim PdfSharpFontLock = new(1, 1);

    /// <summary>現在位置から XLSX ストリームを読み取り、指定された出力シンクへ変換成果物を書き込みます。</summary>
    /// <param name="input">読み取り対象の XLSX データを含むストリームです。メソッドはこのストリームを閉じません。</param>
    /// <param name="request">出力形式、シート選択、解像度、および診断ポリシーを指定する変換設定です。</param>
    /// <param name="sink">生成した成果物を受け取り、保存する出力シンクです。</param>
    /// <param name="cancellationToken">変換処理のキャンセルを通知するトークンです。</param>
    /// <returns>すべての成果物の書き込みが完了したときに完了するタスクを返します。</returns>
    public static async Task<ConversionResult> RenderAsync(
        Stream input,
        RenderRequest request,
        IRenderOutputSink sink,
        CancellationToken cancellationToken = default)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (sink is null)
        {
            throw new ArgumentNullException(nameof(sink));
        }

        ValidateRequest(request);
        var diagnostics = new DiagnosticCollector(request.DiagnosticOptions);
        var artifacts = new List<ArtifactMetadata>();
        try
        {
            using var resources = new RenderResourceSession();
            var inputTimer = System.Diagnostics.Stopwatch.StartNew();
            using var source = await WorkbookInputPreparer.ReadAsync(input, request.Input, cancellationToken).ConfigureAwait(false);
            ConversionMetrics.Report("input.ms", inputTimer.Elapsed.TotalMilliseconds);
            var fontManager = ConversionMetrics.Measure("fonts", () => new FontManager(request.FontOptions));
            resources.TextMeasurer = new PdfSharpTextMeasurer(fontManager, cacheLayouts: true);
            var document = ConversionMetrics.Measure("reader", () => new ExcelReader(fontManager).Read(source, diagnostics, request.OutputFormat == OutputFormat.Markdown ? null : request.Selection.SheetNames));
            source.Dispose();
            if (ConversionMetrics.Observer is not null)
            {
                ConversionMetrics.Report("cells", document.Sheets.Sum(sheet => sheet.Cells.Count));
                ConversionMetrics.Report("textCells", document.Sheets.Sum(sheet => sheet.Cells.Values.Count(cell => !string.IsNullOrEmpty(cell.Text))));
                ConversionMetrics.Report("uniqueStyles", document.Sheets.SelectMany(sheet => sheet.Cells.Values).Select(cell => cell.Style).Distinct().Count());
            }

            var sheets = ApplyRanges(SelectSheets(document, request.Selection.SheetNames), request.Selection, diagnostics);
            if (diagnostics.HasFailure)
            {
                throw Failure("Conversion was stopped by diagnostic policy.", null, diagnostics, artifacts);
            }

            if (request.OutputFormat == OutputFormat.Markdown && request.Selection.Pages is not null)
            {
                throw new ArgumentException("Page selection is not supported for Markdown.", nameof(request));
            }

            if (request.OutputFormat == OutputFormat.Markdown)
            {
                _ = new MarkdownHyperlinks(new ReportDocument(sheets.Select(s => s.Sheet).ToArray()), request.Hyperlinks, diagnostics);
                if (diagnostics.HasFailure)
                {
                    throw Failure("Conversion was stopped by diagnostic policy.", null, diagnostics, artifacts);
                }

                await WriteMarkdownAsync(sheets, request.Hyperlinks, sink, artifacts, cancellationToken).ConfigureAwait(false);
                return new(
                    ConversionManifest.SchemaVersion,
                    "Completed",
                    sheets.Select(x => x.Sheet.Name).ToArray(),
                    Array.Empty<RenderPageDescriptor>(),
                    artifacts,
                    diagnostics.ToArray());
            }

            ConversionMetrics.Measure("diagnostics", () =>
            {
                CollectMissingGlyphDiagnostics(sheets, fontManager, diagnostics);
                return true;
            });
            if (diagnostics.HasFailure)
            {
                throw Failure("Conversion was stopped by diagnostic policy.", null, diagnostics, artifacts);
            }

            sheets = sheets.Select(selected => selected with { Sheet = ProjectSheet(selected.Sheet, request) }).ToArray();
            var workbookMetadata = new WorkbookRenderMetadata(document);
            document = new ReportDocument([]);
            ConversionMetrics.Report("modelSelectedCells", sheets.Sum(selected => selected.Sheet.Cells.Count));

            var lockTimer = System.Diagnostics.Stopwatch.StartNew();
            await PdfSharpFontLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            ConversionMetrics.Report("fontLockWait.ms", lockTimer.Elapsed.TotalMilliseconds);
            try
            {
                // PDFsharp caches resolved typefaces process-wide. Reset that cache while conversions are
                // serialized so that this request's font manager is used for both measurement and PDF output.
                GlobalFontSettings.ResetFontManagement();
                GlobalFontSettings.FontResolver = new PdfSharpFontResolver(fontManager);

                var pages = request.ImageLayout == ImageLayoutMode.Continuous
                    ? LayoutContinuous(sheets, request.Dpi, fontManager)
                    : LayoutPages(sheets, request.Dpi, fontManager);
                var selectedPages = SelectPages(pages, request.Selection.Pages).Select(page =>
                    ConversionMetrics.Measure("pagePreflight", () => PreflightPage(page, request, fontManager, diagnostics, cancellationToken))).ToArray();
                if (request.OutputFormat == OutputFormat.Pdf && request.Hyperlinks == HyperlinkMode.Preserve)
                {
                    selectedPages = ConversionMetrics.Measure("links", () => ResolvePdfLinks(selectedPages, workbookMetadata, diagnostics)).ToArray();
                }

                if (diagnostics.HasFailure)
                {
                    throw Failure("Conversion was stopped by diagnostic policy.", null, diagnostics, artifacts);
                }

                if (request.OutputFormat == OutputFormat.Pdf)
                {
                    await WritePdfAsync(selectedPages, fontManager, diagnostics, sink, artifacts, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await WritePageArtifactsAsync(selectedPages, request, fontManager, sink, artifacts, cancellationToken).ConfigureAwait(false);
                }

                return new(
                    ConversionManifest.SchemaVersion,
                    "Completed",
                    sheets.Select(x => x.Sheet.Name).ToArray(),
                    selectedPages.Select(x => x.Descriptor).ToArray(),
                    artifacts,
                    diagnostics.ToArray());
            }
            finally
            {
                PdfSharpFontLock.Release();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (ConversionException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw Failure("Conversion failed.", exception, diagnostics, artifacts);
        }
    }

    private static void ValidateRequest(RenderRequest request)
    {
        if (request.Buffering is null || request.Selection is null || request.Trim is null || request.Input is null || request.FontOptions is null || request.DiagnosticOptions is null)
        {
            throw new ArgumentException("Required render options must not be null.", nameof(request));
        }

        request.Buffering.Validate();
        if (!Enum.IsDefined(typeof(OutputFormat), request.OutputFormat) || !Enum.IsDefined(typeof(ImageLayoutMode), request.ImageLayout) ||
            !Enum.IsDefined(typeof(HyperlinkMode), request.Hyperlinks))
        {
            throw new ArgumentException("Unknown rendering option value.", nameof(request));
        }

        if (request.Selection.MaxRangeCells <= 0 || !double.IsFinite(request.Trim.PaddingPoints) || request.Trim.PaddingPoints < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Range limits must be positive and padding must be finite and nonnegative.");
        }

        if (request.OutputFormat == OutputFormat.Markdown && (request.Selection.Ranges is not null || request.Trim.Enabled || request.Selection.Pages is not null))
        {
            throw new ArgumentException("Markdown does not support explicit ranges, trimming or page selection.", nameof(request));
        }

        if (double.IsNaN(request.Dpi) || double.IsInfinity(request.Dpi) || request.Dpi <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "DPI must be a positive finite value.");
        }

        if (request.MaxPngPixels <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Maximum PNG pixels must be positive.");
        }

        if (request.ImageLayout == ImageLayoutMode.Continuous)
        {
            if (request.OutputFormat is not (OutputFormat.Png or OutputFormat.Svg))
            {
                throw new ArgumentException("Continuous image layout is supported only for PNG and SVG.", nameof(request));
            }

            if (request.Selection.Pages is not null)
            {
                throw new ArgumentException("Page selection is not supported with continuous image layout.", nameof(request));
            }
        }
    }

    private static IReadOnlyList<SheetPage> LayoutPages(IReadOnlyList<SelectedSheet> sheets, double dpi, FontManager fontManager)
    {
        var pages = new List<SheetPage>();
        foreach (var selected in sheets)
        {
            var plans = new ReportLayoutEngine(RenderResourceSession.Current?.TextMeasurer ?? new PdfSharpTextMeasurer(fontManager, cacheLayouts: true)).Plan(selected.Sheet);
            var count = plans.Sum(plan => plan.Pages.Count);
            var sourceNumber = 0;
            foreach (var plan in plans)
            {
                for (var index = 0; index < plan.Pages.Count; index++)
                {
                    pages.Add(SheetPage.CreatePaginated(selected.Sheet, CreateDescriptor(selected, ++sourceNumber, pages.Count + 1, dpi), plan, index, count));
                }
            }

            if (count == 0)
            {
                pages.Add(SheetPage.CreateEmpty(selected.Sheet, CreateDescriptor(selected, 1, pages.Count + 1, dpi)));
            }
        }

        return pages;
    }

    private static RenderPageDescriptor CreateDescriptor(SelectedSheet selected, int sourceNumber, int documentNumber, double dpi) => new(selected.Index, selected.Sheet.Name, sourceNumber, documentNumber, null, selected.Sheet.PageSettings.Width, selected.Sheet.PageSettings.Height, null, null, dpi)
    {
        RequestedRange = selected.Sheet.RequestedRange,
    };

    private static IReadOnlyList<SheetPage> LayoutContinuous(IReadOnlyList<SelectedSheet> sheets, double dpi, FontManager fontManager) =>
        sheets.Select((selected, index) =>
        {
            var plan = new ContinuousLayoutPlan(selected.Sheet, RenderResourceSession.Current?.TextMeasurer ?? new PdfSharpTextMeasurer(fontManager, cacheLayouts: true));
            var descriptor = CreateDescriptor(selected, 1, index + 1, dpi) with { WidthPoints = plan.Width, HeightPoints = plan.Height };
            return SheetPage.CreateContinuous(selected.Sheet, descriptor, plan);
        }).ToArray();

    private static PageRenderPayload BuildPage(SheetPage page, FontManager fonts, CancellationToken token)
    {
        var measurer = RenderResourceSession.Current?.TextMeasurer ?? new PdfSharpTextMeasurer(fonts, cacheLayouts: true);
        if (page.CanvasPlan is { } canvas)
        {
            return new(ConversionMetrics.MeasureCommands(new DrawCommandGeneratorPass().GenerateContinuous(canvas, measurer)), canvas.SourceRegions);
        }

        var built = page.Plan is { } plan
            ? plan.Build(page.PlanIndex, page.Descriptor.SourcePageNumber, page.SheetPageCount, measurer, token)
            : new RenderPage(1, []);
        built = built with { HeaderFooterTexts = PaginationPass.GetHeaderFooterTexts(page.Sheet, page.Descriptor.SourcePageNumber, page.SheetPageCount, RenderResourceSession.Current?.HeaderFooterTimestamp) };
        var commands = new DrawCommandGeneratorPass().GeneratePage(built);
        ConversionMetrics.Report("renderCells", built.Cells.Count);
        ConversionMetrics.Report("commands", commands.Count);
        return new(commands, built.SourceRegions);
    }

    private static SheetPage PreflightPage(SheetPage page, RenderRequest request, FontManager fonts, DiagnosticCollector diagnostics, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ConversionMetrics.Report("pagePayloadActive", 1);
        try
        {
            var payload = ConversionMetrics.Measure("pageBuild", () => BuildPage(page, fonts, token));
            var final = PrepareViewport(page with { Regions = payload.Regions }, WithCancellation(payload.Commands, token), request, fonts, diagnostics);
            return final with
            {
                Descriptor = final.Descriptor with
                {
                    SourceCellRanges = page.Plan is null && !page.IsContinuous ? null : (final.Regions ?? []).Where(region => region.Cells is not null).Select(region => region.Cells!.Value).ToArray(),
                    SourceRegions = page.Plan is null && !page.IsContinuous ? null : (final.Regions ?? []).Select(region => region.SourceBounds).ToArray(),
                },
            };
        }
        finally
        {
            ConversionMetrics.Report("pagePayloadActive", -1);
        }
    }

    private static void RenderPagePayload(SheetPage page, FontManager fonts, CancellationToken token, Action<IEnumerable<DrawCommand>> render)
    {
        token.ThrowIfCancellationRequested();
        ConversionMetrics.Report("pagePayloadActive", 1);
        try
        {
            var payload = ConversionMetrics.Measure("pageBuild", () => BuildPage(page, fonts, token));
            var commands = page.Descriptor.CropBounds is not null
                ? ApplyViewport(payload.Commands, page.Viewport!, page.Descriptor.SourcePageNumber)
                : payload.Commands;
            render(WithCancellation(commands, token));
        }
        finally
        {
            ConversionMetrics.Report("pagePayloadActive", -1);
        }
    }

    private static IEnumerable<DrawCommand> WithCancellation(IEnumerable<DrawCommand> commands, CancellationToken token)
    {
        var count = 0;
        foreach (var command in commands)
        {
            if ((count++ & 255) == 0)
            {
                token.ThrowIfCancellationRequested();
            }

            yield return command;
        }

        token.ThrowIfCancellationRequested();
    }

    private static IEnumerable<DrawCommand> ApplyViewport(IEnumerable<DrawCommand> commands, PageViewport viewport, int number)
    {
        foreach (var command in commands)
        {
            yield return viewport.Apply([command], number);
        }
    }

    private static (int? Width, int? Height) GetContinuousPixelDimensions(double width, double height, double dpi)
    {
        var scale = dpi / 72d;
        var pixelWidth = Math.Ceiling(width * scale);
        var pixelHeight = Math.Ceiling(height * scale);
        return pixelWidth <= int.MaxValue && pixelHeight <= int.MaxValue
            ? ((int)pixelWidth, (int)pixelHeight)
            : (null, null);
    }

    private static IReadOnlyList<SheetPage> SelectPages(IReadOnlyList<SheetPage> pages, IReadOnlyList<int>? requested)
    {
        if (requested is null)
        {
            return pages.Select((x, i) => x with { Descriptor = x.Descriptor with { OutputPageNumber = i + 1 } }).ToArray();
        }

        if (requested.Count == 0 || requested.Any(x => x <= 0))
        {
            throw new ArgumentException("Page numbers must be positive.");
        }

        var wanted = new HashSet<int>(requested);
        if (wanted.Any(x => x > pages.Count))
        {
            throw new ArgumentOutOfRangeException(nameof(requested), "A selected page is outside the document.");
        }

        return pages.Where(x => wanted.Contains(x.Descriptor.DocumentPageNumber))
            .Select((x, i) => x with { Descriptor = x.Descriptor with { OutputPageNumber = i + 1 } }).ToArray();
    }

    private static IReadOnlyList<SelectedSheet> SelectSheets(ReportDocument document, IReadOnlyList<string>? names)
    {
        if (names is null)
        {
            return document.Sheets.Select((x, i) => new SelectedSheet(i + 1, x)).ToArray();
        }

        if (names.Any(string.IsNullOrEmpty))
        {
            throw new ArgumentException("Sheet names must not be empty.", nameof(names));
        }

        var selected = new List<SelectedSheet>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            if (!seen.Add(name))
            {
                continue;
            }

            var index = document.Sheets.ToList().FindIndex(x => string.Equals(x.Name, name, StringComparison.Ordinal));
            if (index < 0)
            {
                throw new ArgumentException($"Worksheet was not found: {name}", nameof(names));
            }

            selected.Add(new(index + 1, document.Sheets[index]));
        }

        return selected;
    }

    private static async Task WritePdfAsync(IReadOnlyList<SheetPage> pages, FontManager fontManager, DiagnosticCollector diagnostics, IRenderOutputSink sink, List<ArtifactMetadata> artifacts, CancellationToken token)
    {
        var descriptor = new ArtifactDescriptor("pdf", "pdf", "application/pdf", "workbook.pdf");
        await WriteArtifactAsync(
            sink,
            descriptor,
            artifacts,
            stream =>
            {
                using var result = new PdfDocument();
                var renderer = new PdfSharpRenderer(fontManager);
                foreach (var page in pages)
                {
                    token.ThrowIfCancellationRequested();
                    renderer.PageDiagnosticHandler = diagnostic => diagnostics.Add(diagnostic with
                    {
                        SheetName = page.Sheet.Name,
                        SourcePageNumber = page.Descriptor.SourcePageNumber,
                    });
                    ConversionMetrics.Measure("pdfPage", () =>
                    {
                        RenderPagePayload(page, fontManager, token, commands => renderer.AppendPage(
                            result,
                            page.Sheet.PageSettings with { Width = page.Descriptor.WidthPoints, Height = page.Descriptor.HeightPoints },
                            commands));
                        return true;
                    });
                    if (diagnostics.HasFailure)
                    {
                        throw Failure("Conversion was stopped by diagnostic policy.", null, diagnostics, artifacts);
                    }
                }

                foreach (var page in pages)
                {
                    PdfHyperlinkWriter.Write(result, page.Descriptor.OutputPageNumber!.Value, page.Descriptor.HeightPoints, page.Links ?? []);
                }

                token.ThrowIfCancellationRequested();
                ConversionMetrics.Measure("pdfFinalSave", () =>
                {
                    result.Save(stream, false);
                    return true;
                });
                ConversionMetrics.Report("pdfSave", 1);
            },
            token).ConfigureAwait(false);
    }

    private static async Task WritePageArtifactsAsync(IReadOnlyList<SheetPage> pages, RenderRequest request, FontManager fontManager, IRenderOutputSink sink, List<ArtifactMetadata> artifacts, CancellationToken token)
    {
        var extension = request.OutputFormat == OutputFormat.Png ? "png" : "svg";
        var media = request.OutputFormat == OutputFormat.Png ? "image/png" : "image/svg+xml";
        var names = CreateUniqueNames(pages);
        foreach (var page in pages)
        {
            token.ThrowIfCancellationRequested();
            var safe = names[page.Descriptor.SourceSheetIndex];
            var descriptor = new ArtifactDescriptor(
                $"{extension}-{page.Descriptor.OutputPageNumber}",
                extension,
                media,
                page.IsContinuous ? $"{safe}.{extension}" : $"{safe}-{page.Descriptor.SourcePageNumber}.{extension}",
                page.Descriptor.SourcePageNumber,
                page.Descriptor.OutputPageNumber,
                page.IsContinuous,
                page.Descriptor.SourceSheetName,
                page.Descriptor.WidthPoints,
                page.Descriptor.HeightPoints,
                page.Descriptor.PixelWidth,
                page.Descriptor.PixelHeight)
            {
                RequestedRange = page.Descriptor.RequestedRange,
                OriginalWidthPoints = page.Descriptor.OriginalWidthPoints,
                OriginalHeightPoints = page.Descriptor.OriginalHeightPoints,
                CropBounds = page.Descriptor.CropBounds,
                PaddingPoints = page.Descriptor.PaddingPoints,
            };
            await WriteArtifactAsync(
                sink,
                descriptor,
                artifacts,
                stream =>
                {
                    RenderPagePayload(page, fontManager, token, commands =>
                    {
                    if (request.OutputFormat == OutputFormat.Png)
                    {
                        var renderer = new PngRenderer(fontManager);
                        renderer.RenderCanvas(
                            commands,
                            page.Descriptor.WidthPoints,
                            page.Descriptor.HeightPoints,
                            stream,
                            request.Dpi,
                            request.MaxPngPixels);
                    }
                    else
                    {
                        var renderer = new SvgRenderer(fontManager);
                        renderer.RenderCanvas(commands, page.Descriptor.WidthPoints, page.Descriptor.HeightPoints, stream, request.Buffering, token);
                    }
                    });
                },
                token).ConfigureAwait(false);
        }
    }

    private static void CollectMissingGlyphDiagnostics(IReadOnlyList<SelectedSheet> sheets, FontManager fonts, DiagnosticCollector diagnostics)
    {
        foreach (var selected in sheets)
        {
            foreach (var (address, cell) in selected.Sheet.Cells)
            {
                Add(cell.Text, cell.Style.Font, CellName(address), null);
            }

            var shapes = selected.Sheet.Shapes ?? [];
            for (var i = 0; i < shapes.Count; i++)
            {
                var shape = shapes[i];
                if (shape.Text is { } text)
                {
                    Add(text.Text, text.Font, null, $"shape-{i + 1}");
                }
            }

            void Add(string? text, FontStyle style, string? cell, string? objectId)
            {
                if (string.IsNullOrEmpty(text))
                {
                    return;
                }

                var request = new FontRequest(style.Family, style.Bold ? 700 : 400, style.Italic);
                var runs = fonts.ResolveTextRuns(text, request);
                foreach (var run in runs.Where(x => x.MissingIvsGlyph))
                {
                    var sequence = string.Join(" ", ToScalars(run.SourceText).Select(x => $"U+{x:X4}"));
                    diagnostics.Add(new(
                        "MissingIvsGlyph",
                        DiagnosticSeverity.Warning,
                        DiagnosticStage.Layout,
                        $"No bundled IVS font supports {sequence} at UTF-16 offset {run.Utf16Start}; a replacement glyph will be rendered.",
                        selected.Sheet.Name,
                        cell,
                        objectId,
                        UnicodeSequence: sequence));
                }

                foreach (var run in runs.Where(x => x.MissingPrivateUseGlyph))
                {
                    var sequence = string.Join(" ", ToScalars(run.SourceText).Select(x => $"U+{x:X4}"));
                    diagnostics.Add(new(
                        "MissingPrivateUseGlyph",
                        DiagnosticSeverity.Warning,
                        DiagnosticStage.Layout,
                        $"No explicitly configured font supports private-use character {sequence} at UTF-16 offset {run.Utf16Start}; a replacement glyph will be rendered.",
                        selected.Sheet.Name,
                        cell,
                        objectId,
                        UnicodeSequence: sequence));
                }
            }
        }

        static IEnumerable<int> ToScalars(string value)
        {
            for (var i = 0; i < value.Length; i++)
            {
                yield return char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1])
                    ? char.ConvertToUtf32(value[i], value[++i]) : value[i];
            }
        }

        static string CellName(CellAddress address)
        {
            var column = address.Column + 1;
            var name = string.Empty;
            while (column > 0)
            {
                column--;
                name = (char)('A' + (column % 26)) + name;
                column /= 26;
            }

            return name + (address.Row + 1);
        }
    }

    private static Task WriteMarkdownAsync(IReadOnlyList<SelectedSheet> sheets, HyperlinkMode hyperlinks, IRenderOutputSink sink, List<ArtifactMetadata> artifacts, CancellationToken token)
    {
        var descriptor = new ArtifactDescriptor("markdown", "markdown", "text/markdown", "workbook.md");
        if (sink is IMarkdownDocumentOutputSink markdownSink)
        {
            return WriteLegacyMarkdownAsync(markdownSink, sheets, descriptor, artifacts, token);
        }

        return WriteArtifactAsync(
            sink,
            descriptor,
            artifacts,
            stream =>
            {
                using var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, leaveOpen: true);
                writer.Write(MarkdownExporter.Build(
                    new ReportDocument(sheets.Select(s => s.Sheet).ToArray()),
                    string.Empty,
                    "workbook.xlsx",
                    new MarkdownExportOptions { ExportImages = false, Hyperlinks = hyperlinks }));
            },
            token);
    }

    private static async Task WriteLegacyMarkdownAsync(
        IMarkdownDocumentOutputSink sink,
        IReadOnlyList<SelectedSheet> sheets,
        ArtifactDescriptor descriptor,
        List<ArtifactMetadata> artifacts,
        CancellationToken token)
    {
        await sink.WriteMarkdownAsync(new ReportDocument(sheets.Select(x => x.Sheet).ToArray()), token).ConfigureAwait(false);
        artifacts.Add(new(descriptor, sink.GetMarkdownByteLength()));
    }

    private static async Task WriteArtifactAsync(IRenderOutputSink sink, ArtifactDescriptor descriptor, List<ArtifactMetadata> artifacts, Action<Stream> write, CancellationToken token)
    {
        Stream? stream = null;
        try
        {
            stream = await sink.OpenAsync(descriptor, token).ConfigureAwait(false);
            var counted = new CountingStream(stream);
            write(counted);
            await counted.FlushAsync(token).ConfigureAwait(false);
            var length = counted.BytesWritten;
            await sink.CompleteAsync(descriptor, length, token).ConfigureAwait(false);
            artifacts.Add(new(descriptor, length));
        }
        catch (Exception error)
        {
            if (stream is not null)
            {
                try
                {
                    await sink.AbortAsync(descriptor, error, token).ConfigureAwait(false);
                }
                catch (Exception abortError)
                {
                    error.Data["AbortException"] = abortError;
                }
            }

            throw;
        }
    }

    private static ConversionException Failure(string message, Exception? error, DiagnosticCollector diagnostics, List<ArtifactMetadata> artifacts) =>
        new(message, error, diagnostics.ToArray(), artifacts.ToArray());

    private static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }).ToHashSet();
        var safe = new string(name.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim().Trim('.');
        return string.IsNullOrEmpty(safe) ? "sheet" : safe;
    }

    private static IReadOnlyDictionary<int, string> CreateUniqueNames(IEnumerable<SheetPage> pages)
    {
        var result = new Dictionary<int, string>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in pages)
        {
            if (result.ContainsKey(page.Descriptor.SourceSheetIndex))
            {
                continue;
            }

            var baseName = SafeName(page.Sheet.Name);
            var name = baseName;
            for (var suffix = 2; !used.Add(name); suffix++)
            {
                name = baseName + "-" + suffix;
            }

            result.Add(page.Descriptor.SourceSheetIndex, name);
        }

        return result;
    }

    private sealed record SelectedSheet(int Index, ReportSheet Sheet);

    private sealed record SheetPage
    {
        private SheetPage(ReportSheet sheet, RenderPageDescriptor descriptor, SheetLayoutPlan? plan, ContinuousLayoutPlan? canvasPlan, int planIndex, int sheetPageCount)
        {
            if (plan is not null && canvasPlan is not null)
            {
                throw new ArgumentException("A page cannot have both paginated and continuous plans.");
            }

            if (planIndex < 0 || (plan is not null ? planIndex >= plan.Pages.Count : planIndex != 0))
            {
                throw new ArgumentOutOfRangeException(nameof(planIndex));
            }

            if (sheetPageCount <= 0 || descriptor.SourcePageNumber <= 0 || descriptor.SourcePageNumber > sheetPageCount ||
                (plan is null && (sheetPageCount != 1 || descriptor.SourcePageNumber != 1)))
            {
                throw new ArgumentOutOfRangeException(nameof(sheetPageCount));
            }

            Sheet = sheet;
            Descriptor = descriptor;
            Plan = plan;
            CanvasPlan = canvasPlan;
            PlanIndex = planIndex;
            SheetPageCount = sheetPageCount;
        }

        public ReportSheet Sheet { get; }

        public RenderPageDescriptor Descriptor { get; init; }

        public bool IsContinuous => CanvasPlan is not null;

        public IReadOnlyList<PageSourceRegion>? Regions { get; init; }

        public PageViewport? Viewport { get; init; }

        public IReadOnlyList<ResolvedPdfHyperlink>? Links { get; init; }

        public SheetLayoutPlan? Plan { get; }

        public int PlanIndex { get; }

        public int SheetPageCount { get; }

        public ContinuousLayoutPlan? CanvasPlan { get; }

        public static SheetPage CreatePaginated(ReportSheet sheet, RenderPageDescriptor descriptor, SheetLayoutPlan plan, int planIndex, int sheetPageCount) => new(sheet, descriptor, plan ?? throw new ArgumentNullException(nameof(plan)), null, planIndex, sheetPageCount);

        public static SheetPage CreateContinuous(ReportSheet sheet, RenderPageDescriptor descriptor, ContinuousLayoutPlan canvasPlan) =>
            new(sheet, descriptor, null, canvasPlan ?? throw new ArgumentNullException(nameof(canvasPlan)), 0, 1) { Regions = canvasPlan.SourceRegions };

        public static SheetPage CreateEmpty(ReportSheet sheet, RenderPageDescriptor descriptor) => new(sheet, descriptor, null, null, 0, 1);
    }

    private sealed record PageRenderPayload(IEnumerable<DrawCommand> Commands, IReadOnlyList<PageSourceRegion> Regions);

    private sealed class CountingStream : Stream
    {
        private readonly Stream _inner;

        public CountingStream(Stream inner) => _inner = inner;

        public long BytesWritten { get; private set; }

        public override bool CanRead => _inner.CanRead;

        public override bool CanSeek => _inner.CanSeek;

        public override bool CanWrite => _inner.CanWrite;

        public override long Length => _inner.Length;

        public override long Position { get => _inner.Position; set => _inner.Position = value; }

        public override void Flush() => _inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

        public override void SetLength(long value) => _inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count)
        {
            _inner.Write(buffer, offset, count);
            BytesWritten += count;
        }

        public override void WriteByte(byte value)
        {
            _inner.WriteByte(value);
            BytesWritten++;
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await _inner.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
            BytesWritten += count;
        }

        protected override void Dispose(bool disposing)
        {
            // Sink ownership: disposing this wrapper must never close the underlying output.
        }
    }
}
