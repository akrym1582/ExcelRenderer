using ExcelRenderer.Fonts;
using ExcelRenderer.Rendering;

namespace ExcelRenderer.Http.Sample;

/// <summary>Demonstrates direct and diagnostic-validated asynchronous PDF responses.</summary>
public static class PdfHttpEndpoints
{
    private static readonly HashSet<string> RejectedCodes = new(StringComparer.Ordinal)
    {
        "FormulaCachedValueMissing",
        "ImageDecodeFailed",
    };

    /// <summary>Maps PDF endpoints for a server-owned workbook and font.</summary>
    /// <param name="app">The host application.</param>
    /// <param name="workbookPath">The readable XLSX path.</param>
    /// <param name="fontPath">The regular font path.</param>
    public static void Map(WebApplication app, string workbookPath, string fontPath)
    {
        app.MapGet("/pdf", (HttpContext context) => HandleAsync(context, workbookPath, fontPath, false, false));
        app.MapGet("/core-pdf", (HttpContext context) => HandleAsync(context, workbookPath, fontPath, true, false));
        app.MapGet("/pdf-validated", (HttpContext context) => HandleAsync(context, workbookPath, fontPath, false, true));
        app.MapGet("/core-pdf-validated", (HttpContext context) => HandleAsync(context, workbookPath, fontPath, true, true));
    }

    /// <summary>Runs one request; the optional directory also permits deterministic cleanup tests.</summary>
    /// <param name="context">The response and cancellation owner.</param>
    /// <param name="workbookPath">The server-owned workbook.</param>
    /// <param name="fontPath">The server-owned font.</param>
    /// <param name="core">Whether to use Core.</param>
    /// <param name="validated">Whether to check diagnostics before sending bytes.</param>
    /// <param name="temporaryDirectory">Optional application-owned temporary directory.</param>
    /// <returns>The completed response operation.</returns>
    public static async Task HandleAsync(HttpContext context, string workbookPath, string fontPath, bool core, bool validated, string? temporaryDirectory = null)
    {
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("PdfConversion");
        var token = context.RequestAborted;
        try
        {
            token.ThrowIfCancellationRequested();

            // A missing configured font is a server fault, regardless of workbook validity.
            using (File.OpenRead(fontPath))
            {
            }

            using var input = File.OpenRead(workbookPath);
            if (validated)
            {
                var temporary = Path.Combine(temporaryDirectory ?? Path.GetTempPath(), $"excel-pdf-{Guid.NewGuid():N}.tmp");
                try
                {
                    await using var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536, FileOptions.Asynchronous);
                    var codes = await ConvertAsync(input, output, fontPath, core, logger, token);
                    var rejected = codes.Where(pair => RejectedCodes.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value);
                    if (rejected.Count != 0)
                    {
                        await Results.Problem(statusCode: 422, title: "PDF rejected by application diagnostic policy", extensions: new Dictionary<string, object?> { ["diagnostics"] = rejected }).ExecuteAsync(context);
                        return;
                    }

                    output.Position = 0;
                    SetHeaders(context);
                    await output.CopyToAsync(context.Response.Body, token);
                }
                finally
                {
                    File.Delete(temporary);
                }
            }
            else
            {
                SetHeaders(context);
                await ConvertAsync(input, context.Response.Body, fontPath, core, logger, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            logger.LogInformation("PDF request cancelled");
            throw;
        }
        catch (Exception error) when (!context.Response.HasStarted)
        {
            // Both paths are server configuration, so malformed workbooks and fonts are server faults.
            var cause = error is ConversionException conversion ? conversion.InnerException ?? conversion : error;
            logger.LogError("PDF conversion failed: {ErrorType}", cause.GetType().Name);
            context.Response.Clear();
            await Results.Problem(statusCode: 500, title: "PDF conversion failed").ExecuteAsync(context);
        }
    }

    private static async Task<IReadOnlyDictionary<string, int>> ConvertAsync(Stream input, Stream output, string fontPath, bool core, ILogger logger, CancellationToken token)
    {
        if (core)
        {
            var result = await Core.ExcelConverter.ConvertAsync(input, output, new Core.PdfExportOptions { FontFilePath = fontPath }, token);
            foreach (var diagnostic in result.Diagnostics)
            {
                logger.LogWarning("PDF diagnostic {Code} sheet={Sheet} page={Page}", diagnostic.Code, diagnostic.SheetName, diagnostic.PageNumber);
            }

            return result.Diagnostics.GroupBy(diagnostic => diagnostic.Code, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count());
        }
        else
        {
            var result = await ExcelConverter.RenderAsync(
                input,
                new RenderRequest
                {
                    OutputFormat = OutputFormat.Pdf,
                    FontOptions = new FontOptions
                    {
                        FontFiles = [fontPath],
                        AllowSystemFonts = false,
                        UseFontPack = false,
                    },
                },
                new SingleStreamOutputSink(output),
                token);
            foreach (var diagnostic in result.Diagnostics)
            {
                logger.LogWarning("PDF diagnostic {Code} severity={Severity} stage={Stage} sheet={Sheet} cell={Cell} page={Page} count={Count}", diagnostic.Code, diagnostic.Severity, diagnostic.Stage, diagnostic.SheetName, diagnostic.CellRange, diagnostic.SourcePageNumber, diagnostic.OccurrenceCount);
            }

            if (result.CompletionStatus != "Completed")
            {
                throw new InvalidOperationException("Conversion did not complete.");
            }

            return result.Diagnostics.GroupBy(diagnostic => diagnostic.Code, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Sum(diagnostic => diagnostic.OccurrenceCount));
        }
    }

    private static void SetHeaders(HttpContext context)
    {
        context.Response.ContentType = "application/pdf";
        context.Response.Headers.ContentDisposition = "attachment; filename=report.pdf";
    }
}
