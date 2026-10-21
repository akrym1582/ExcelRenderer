using ExcelRenderer.Fonts;
using ExcelRenderer.Rendering;

namespace ExcelRenderer.Http.Sample;

/// <summary>Demonstrates direct asynchronous PDF output to Kestrel response streams.</summary>
public static class PdfHttpEndpoints
{
    /// <summary>Maps main-library and Core PDF endpoints for a server-owned workbook and font.</summary>
    /// <param name="app">The host application.</param>
    /// <param name="workbookPath">The readable XLSX path.</param>
    /// <param name="fontPath">The regular font path.</param>
    public static void Map(WebApplication app, string workbookPath, string fontPath)
    {
        app.MapGet("/pdf", async (HttpContext context) =>
        {
            using var xlsx = File.OpenRead(workbookPath);
            SetHeaders(context);
            await ExcelConverter.RenderAsync(
                xlsx,
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
                new SingleStreamOutputSink(context.Response.Body),
                context.RequestAborted);
        });
        app.MapGet("/core-pdf", async (HttpContext context) =>
        {
            using var xlsx = File.OpenRead(workbookPath);
            SetHeaders(context);
            await Core.ExcelConverter.ConvertAsync(
                xlsx,
                context.Response.Body,
                new Core.PdfExportOptions { FontFilePath = fontPath },
                context.RequestAborted);
        });
    }

    private static void SetHeaders(HttpContext context)
    {
        context.Response.ContentType = "application/pdf";
        context.Response.Headers.ContentDisposition = "attachment; filename=report.pdf";
    }
}
