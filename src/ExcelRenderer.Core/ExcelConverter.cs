using ExcelRenderer.Core.Drawing;
using ExcelRenderer.Core.Excel;
using ExcelRenderer.Core.Fonts;
using ExcelRenderer.Core.Input;
using ExcelRenderer.Core.Layout;
using ExcelRenderer.Core.Pdf;
using ExcelRenderer.Core.Rendering;

namespace ExcelRenderer.Core;

/// <summary>Converts XLSX streams to one PDF using one caller-supplied font.</summary>
public static class ExcelConverter
{
    /// <summary>Reads from the input's current position and writes a PDF without closing either stream.</summary>
    /// <param name="xlsx">The readable XLSX stream.</param>
    /// <param name="pdf">An empty writable, seekable stream positioned at zero.</param>
    /// <param name="options">The required font and optional sheet/input settings.</param>
    /// <param name="cancellationToken">The conversion cancellation token.</param>
    /// <returns>The page count and nonfatal diagnostics.</returns>
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

        if (!xlsx.CanRead || !pdf.CanWrite || !pdf.CanSeek || pdf.Position != 0 || pdf.Length != 0)
        {
            throw new ArgumentException("Input must be readable; output must be writable, seekable, empty, and positioned at zero.");
        }

        if (string.IsNullOrWhiteSpace(options.FontFilePath) || options.Input is null)
        {
            throw new ArgumentException("FontFilePath and Input are required.", nameof(options));
        }

        (options.ReadOptions ?? throw new ArgumentException("ReadOptions is required.", nameof(options))).Validate();
        cancellationToken.ThrowIfCancellationRequested();
        using var prepared = await WorkbookInputPreparer.ReadAsync(xlsx, options.Input, cancellationToken).ConfigureAwait(false);
        using var font = new SingleFontContext(options.FontFilePath);
        var diagnostics = new DiagnosticCollector();
        var reader = new CoreExcelReader(font);
        var report = reader.Read(prepared, diagnostics, options.SheetName is null ? null : new[] { options.SheetName }, cancellationToken, options.ReadOptions);
        cancellationToken.ThrowIfCancellationRequested();
        var measurer = new SkiaTextMeasurer(font);
        var engine = new ReportLayoutEngine(measurer);
        var plans = report.Sheets.Select(sheet => (Sheet: sheet, Plans: engine.Plan(sheet))).ToArray();
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
        renderer.Complete(destination);
        cancellationToken.ThrowIfCancellationRequested();
        return new(number, diagnostics.ToArray().Select(item => new ConversionDiagnostic(item.Code, item.Message, item.SheetName, item.SourcePageNumber)).ToArray());
    }
}
