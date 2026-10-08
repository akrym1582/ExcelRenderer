using ExcelRenderer.Slim.Drawing;
using ExcelRenderer.Slim.Excel;
using ExcelRenderer.Slim.Fonts;
using ExcelRenderer.Slim.Input;
using ExcelRenderer.Slim.Layout;
using ExcelRenderer.Slim.Pdf;
using ExcelRenderer.Slim.Rendering;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace ExcelRenderer.Slim;

/// <summary>Converts XLSX streams to one PDF using one caller-supplied font.</summary>
public static class SlimExcelConverter
{
    private static readonly SemaphoreSlim FontGate = new(1, 1);

    /// <summary>Reads from the input's current position and writes a PDF without closing either stream.</summary>
    /// <param name="xlsx">The readable XLSX stream.</param>
    /// <param name="pdf">An empty writable, seekable stream positioned at zero.</param>
    /// <param name="options">The required font and optional sheet/input settings.</param>
    /// <param name="cancellationToken">The conversion cancellation token.</param>
    /// <returns>The page count and nonfatal diagnostics.</returns>
    public static async Task<SlimPdfResult> ConvertAsync(Stream xlsx, Stream pdf, SlimPdfOptions options, CancellationToken cancellationToken = default)
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

        cancellationToken.ThrowIfCancellationRequested();
        using var prepared = await WorkbookInputPreparer.ReadAsync(xlsx, options.Input, cancellationToken).ConfigureAwait(false);
        await FontGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var font = new SingleFontContext(options.FontFilePath);
            GlobalFontSettings.ResetFontManagement();
            GlobalFontSettings.FontResolver = font.Resolver;
            try
            {
                font.ValidatePdfFont();
                var diagnostics = new DiagnosticCollector();
                var reader = new ExcelReader(font);
                var report = reader.Read(prepared, diagnostics, options.SheetName is null ? null : new[] { options.SheetName }, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                var measurer = new PdfSharpTextMeasurer(font);
                var engine = new ReportLayoutEngine(measurer);
                var plans = report.Sheets.Select(sheet => (Sheet: sheet, Plans: engine.Plan(sheet))).ToArray();
                var renderer = new PdfSharpRenderer(font) { DiagnosticHandler = diagnostics.Add };
                var generator = new DrawCommandGeneratorPass();
                var timestamp = DateTime.Now;
                using var images = new ImageResources();
                using var document = new PdfDocument();
                var number = 0;
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
                                renderer.AppendPage(document, entry.Sheet.PageSettings, generator.GeneratePage(page, measurer));
                            }
                            finally
                            {
                                ConversionMetrics.Report("pagePayloadActive", 0);
                            }
                        }
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                ConversionMetrics.Report("pdfSave", 1);
                using var destination = new CancellationWriteStream(pdf, cancellationToken);
                document.Save(destination, false);
                cancellationToken.ThrowIfCancellationRequested();
                return new(number, diagnostics.ToArray().Select(item => new SlimDiagnostic(item.Code, item.Message, item.SheetName, item.SourcePageNumber)).ToArray());
            }
            finally
            {
                GlobalFontSettings.ResetFontManagement();
            }
        }
        finally
        {
            FontGate.Release();
        }
    }
}
