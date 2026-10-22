using ExcelRenderer.Core.Drawing;
using ExcelRenderer.Core.Excel;
using ExcelRenderer.Core.Fonts;
using ExcelRenderer.Core.Layout;
using ExcelRenderer.Core.Pdf;
using ExcelRenderer.Core.Rendering;

namespace ExcelRenderer.Core;

/// <summary>Converts XLSX streams to one PDF using one caller-supplied font.</summary>
public static class ExcelConverter
{
    /// <summary>Reads from the input's current position and writes a PDF without closing either stream.</summary>
    /// <param name="xlsx">The readable XLSX stream.</param>
    /// <param name="pdf">A writable stream; seekable destinations must be empty and positioned at zero.</param>
    /// <param name="options">The required font and optional sheet/input settings.</param>
    /// <param name="cancellationToken">The conversion cancellation token.</param>
    /// <returns>The page count and nonfatal diagnostics.</returns>
    /// <remarks>
    /// Final PDF writes are awaited and support nonseekable streams with synchronous I/O disabled.
    /// The output is not flushed or closed. Skia generates a complete memory/file spool before final
    /// transmission; workbook reading, layout and drawing remain synchronous. Failure may leave partial output.
    /// </remarks>
    public static async Task<ConversionResult> ConvertAsync(Stream xlsx, Stream pdf, PdfExportOptions options, CancellationToken cancellationToken = default)
    {
        if (xlsx is null)
        {
            throw new ArgumentNullException(nameof(xlsx));
        }

        if (pdf is null)
        {
            throw new ArgumentNullException(nameof(pdf));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (!xlsx.CanRead || !pdf.CanWrite || (pdf.CanSeek && (pdf.Position != 0 || pdf.Length != 0)))
        {
            throw new ArgumentException("Input must be readable; output must be writable and, if seekable, empty and positioned at zero.");
        }

        if (string.IsNullOrWhiteSpace(options.FontFilePath) || options.Input is null)
        {
            throw new ArgumentException("FontFilePath and Input are required.", nameof(options));
        }

        if (options.FontCollectionIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "FontCollectionIndex must be nonnegative.");
        }

        if (options.ReadOptions is null)
        {
            throw new ArgumentException("ReadOptions is required.", nameof(options));
        }

        if (options.SheetName is not null && options.SheetNames is not null)
        {
            throw new ArgumentException("SheetName and SheetNames cannot be combined.", nameof(options));
        }

        var sheetNames = options.SheetNames ?? (options.SheetName is null ? null : new[] { options.SheetName });
        if (sheetNames is not null && (sheetNames.Count == 0 || sheetNames.Any(string.IsNullOrEmpty)))
        {
            throw new ArgumentException("SheetNames must contain at least one nonempty worksheet name.", nameof(options));
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var prepared = await OpenXml.Reader.Input.WorkbookInputPreparer.ReadAsync(
            xlsx,
            options.Input,
            cancellationToken,
            () => ConversionMetrics.Report("buffer.spillCount", 1)).ConfigureAwait(false);
        using var font = new SingleFontContext(options.FontFilePath, options.FontCollectionIndex, cancellationToken);
        var diagnostics = new DiagnosticCollector();
        var reader = new CoreExcelReader(font, diagnostics);
        var report = ConversionMetrics.Measure("reader", () => reader.Read(prepared, diagnostics, sheetNames, cancellationToken, options.ReadOptions));
        cancellationToken.ThrowIfCancellationRequested();
        var measurer = new SkiaTextMeasurer(font);
        var engine = new ReportLayoutEngine(measurer);
        var selectedSheets = sheetNames is null
            ? report.Sheets
            : sheetNames.Distinct(StringComparer.Ordinal).Select(name => report.Sheets.Single(sheet => sheet.Name == name)).ToArray();
        var plans = selectedSheets.Select(sheet => (Sheet: sheet, Plans: engine.Plan(sheet))).ToArray();
        using var renderer = new CorePdfRenderer(font, cancellationToken) { DiagnosticHandler = diagnostics.Add };
        var generator = new DrawCommandGeneratorPass();
        var timestamp = DateTime.Now;
        using var images = new ImageResources();
        var number = 0;
        for (var pass = 0; pass < 2; pass++)
        {
            if (pass == 1)
            {
                renderer.Prepare();
            }

            number = 0;
            foreach (var entry in plans)
            {
                var sheetCount = entry.Plans.Sum(plan => plan.Pages.Count);
                var sheetNumber = 0;
                foreach (var plan in entry.Plans)
                {
                    for (var index = 0; index < plan.Pages.Count; index++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        ConversionMetrics.Report("pagePayloadActive", 1);
                        try
                        {
                            var page = plan.Build(index, ++number, sheetCount, measurer, cancellationToken) with
                            {
                                HeaderFooterTexts = PaginationPass.GetHeaderFooterTexts(entry.Sheet, ++sheetNumber, sheetCount, timestamp),
                            };
                            var commands = generator.GeneratePage(page, measurer);
                            if (pass == 0)
                            {
                                renderer.Collect(commands);
                            }
                            else
                            {
                                renderer.AppendPage(entry.Sheet.PageSettings, commands);
                            }
                        }
                        finally
                        {
                            ConversionMetrics.Report("pagePayloadActive", 0);
                        }
                    }
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var destination = new CancellationWriteStream(pdf, cancellationToken);
        await ConversionMetrics.MeasureAsync("pdfFinalSave", () => renderer.CompleteAsync(destination)).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return new(number, diagnostics.ToArray().Select(item => new ConversionDiagnostic(item.Code, item.Message, item.SheetName, item.SourcePageNumber)).ToArray());
    }
}
