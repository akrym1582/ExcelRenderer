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
using PdfSharp.Pdf.IO;

namespace ExcelRenderer;

/// <summary>Excel ブックをストリームから非同期に読み取り、指定された出力先へ変換します。</summary>
public static partial class ExcelConverter
{
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
            var source = await WorkbookInputPreparer.ReadAsync(input, request.Input, cancellationToken).ConfigureAwait(false);
            var document = new ExcelReader().Read(source, diagnostics);
            var sheets = SelectSheets(document, request.Selection.SheetNames);
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
                await WriteMarkdownAsync(sheets, sink, artifacts, cancellationToken).ConfigureAwait(false);
                return new(
                    ConversionManifest.SchemaVersion,
                    "Completed",
                    sheets.Select(x => x.Sheet.Name).ToArray(),
                    Array.Empty<RenderPageDescriptor>(),
                    artifacts,
                    diagnostics.ToArray());
            }

            var pages = request.ImageLayout == ImageLayoutMode.Continuous
                ? LayoutContinuous(sheets, request.Dpi, request.FontOptions)
                : LayoutPages(sheets, request.Dpi, request.FontOptions);
            var selectedPages = SelectPages(pages, request.Selection.Pages);
            if (request.OutputFormat == OutputFormat.Pdf)
            {
                await WritePdfAsync(selectedPages, sink, artifacts, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await WritePageArtifactsAsync(selectedPages, request, sink, artifacts, cancellationToken).ConfigureAwait(false);
            }

            return new(
                ConversionManifest.SchemaVersion,
                "Completed",
                sheets.Select(x => x.Sheet.Name).ToArray(),
                selectedPages.Select(x => x.Descriptor).ToArray(),
                artifacts,
                diagnostics.ToArray());
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

    private static IReadOnlyList<SheetPage> LayoutPages(IReadOnlyList<SelectedSheet> sheets, double dpi, FontOptions fontOptions)
    {
        var fontManager = new FontManager(fontOptions);
        GlobalFontSettings.FontResolver ??= new PdfSharpFontResolver(fontManager);
        var pages = new List<SheetPage>();
        var documentPage = 0;
        foreach (var selected in sheets)
        {
            var commands = new DrawCommandGeneratorPass().Generate(
                new ReportLayoutEngine(new PdfSharpTextMeasurer()).Layout(selected.Sheet));
            var groups = commands.GroupBy(x => x.PageNumber).OrderBy(x => x.Key).ToArray();
            if (groups.Length == 0)
            {
                var width = selected.Sheet.PageSettings.Width;
                var height = selected.Sheet.PageSettings.Height;
                pages.Add(
                    new(
                        selected.Sheet,
                        Array.Empty<DrawCommand>(),
                        new(
                            selected.Index,
                            selected.Sheet.Name,
                            1,
                            ++documentPage,
                            null,
                            width,
                            height,
                            checked((int)Math.Ceiling(width * dpi / 72d)),
                            checked((int)Math.Ceiling(height * dpi / 72d)),
                            dpi)));
                continue;
            }

            foreach (var group in groups)
            {
                var sourcePage = group.Key == 0 ? 1 : group.Key;
                var width = selected.Sheet.PageSettings.Width;
                var height = selected.Sheet.PageSettings.Height;
                var pixels = checked((int)Math.Ceiling(width * dpi / 72d));
                var pixelHeight = checked((int)Math.Ceiling(height * dpi / 72d));
                pages.Add(
                    new(
                        selected.Sheet,
                        group.ToArray(),
                        new(
                            selected.Index,
                            selected.Sheet.Name,
                            sourcePage,
                            ++documentPage,
                            null,
                            width,
                            height,
                            pixels,
                            pixelHeight,
                            dpi)));
            }
        }

        return pages;
    }

    private static IReadOnlyList<SheetPage> LayoutContinuous(IReadOnlyList<SelectedSheet> sheets, double dpi, FontOptions fontOptions)
    {
        var fontManager = new FontManager(fontOptions);
        GlobalFontSettings.FontResolver ??= new PdfSharpFontResolver(fontManager);
        var pages = new List<SheetPage>();
        var documentPage = 0;
        foreach (var selected in sheets)
        {
            var layout = new ReportLayoutEngine(new PdfSharpTextMeasurer()).LayoutContinuous(selected.Sheet);
            var width = layout.Width;
            var height = layout.Height;
            var (pixelWidth, pixelHeight) = GetContinuousPixelDimensions(width, height, dpi);
            pages.Add(new(
                selected.Sheet,
                new DrawCommandGeneratorPass().Generate(layout.Document),
                new(selected.Index, selected.Sheet.Name, 1, ++documentPage, documentPage, width, height, pixelWidth, pixelHeight, dpi),
                IsContinuous: true));
        }

        return pages;
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

    private static async Task WritePdfAsync(IReadOnlyList<SheetPage> pages, IRenderOutputSink sink, List<ArtifactMetadata> artifacts, CancellationToken token)
    {
        var descriptor = new ArtifactDescriptor("pdf", "pdf", "application/pdf", "workbook.pdf");
        await WriteArtifactAsync(
            sink,
            descriptor,
            artifacts,
            stream =>
            {
                using var result = new PdfDocument();
                foreach (var page in pages)
                {
                    using var rendered = new MemoryStream();
                    new PdfSharpRenderer().Render(page.Commands, page.Sheet.PageSettings, rendered);
                    rendered.Position = 0;
                    using var source = PdfReader.Open(rendered, PdfDocumentOpenMode.Import);
                    result.AddPage(source.Pages[0]);
                }

                result.Save(stream, false);
            },
            token).ConfigureAwait(false);
    }

    private static async Task WritePageArtifactsAsync(IReadOnlyList<SheetPage> pages, RenderRequest request, IRenderOutputSink sink, List<ArtifactMetadata> artifacts, CancellationToken token)
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
                page.Descriptor.PixelHeight);
            await WriteArtifactAsync(
                sink,
                descriptor,
                artifacts,
                stream =>
                {
                    var fontManager = new FontManager(request.FontOptions);
                    if (request.OutputFormat == OutputFormat.Png)
                    {
                        var renderer = new PngRenderer(fontManager);
                        if (page.IsContinuous)
                        {
                            renderer.RenderCanvas(
                                page.Commands,
                                page.Descriptor.WidthPoints,
                                page.Descriptor.HeightPoints,
                                stream,
                                request.Dpi,
                                request.MaxPngPixels);
                        }
                        else
                        {
                            renderer.RenderPage(page.Commands, page.Sheet.PageSettings, stream, request.Dpi);
                        }
                    }
                    else
                    {
                        var renderer = new SvgRenderer(fontManager);
                        if (page.IsContinuous)
                        {
                            renderer.RenderCanvas(page.Commands, page.Descriptor.WidthPoints, page.Descriptor.HeightPoints, stream);
                        }
                        else
                        {
                            renderer.RenderPage(page.Commands, page.Sheet.PageSettings, stream);
                        }
                    }
                },
                token).ConfigureAwait(false);
        }
    }

    private static Task WriteMarkdownAsync(IReadOnlyList<SelectedSheet> sheets, IRenderOutputSink sink, List<ArtifactMetadata> artifacts, CancellationToken token)
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
                foreach (var sheet in sheets)
                {
                    writer.WriteLine("## Sheet: " + sheet.Sheet.Name);
                    foreach (var cell in sheet.Sheet.Cells.OrderBy(x => x.Key.Row).ThenBy(x => x.Key.Column))
                    {
                        if (!string.IsNullOrEmpty(cell.Value.Text))
                        {
                            writer.WriteLine(cell.Value.Text);
                        }
                    }

                    writer.WriteLine();
                }
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

    private sealed record SheetPage(ReportSheet Sheet, IReadOnlyList<DrawCommand> Commands, RenderPageDescriptor Descriptor, bool IsContinuous = false);

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
