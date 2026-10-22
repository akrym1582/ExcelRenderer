using ExcelRenderer.Http.Sample;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

if (args.Length == 4 && args[0] == "--file")
{
    try
    {
        var result = await PdfFileExample.ConvertAsync(args[1], args[2], args[3], cancellation.Token);
        Console.WriteLine($"{result.Pages.Count} pages: {args[2]}");
        foreach (var diagnostic in result.Diagnostics)
        {
            Console.Error.WriteLine($"{diagnostic.Code}: sheet={diagnostic.SheetName} page={diagnostic.SourcePageNumber} count={diagnostic.OccurrenceCount}");
        }
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
    {
        Console.Error.WriteLine("Conversion cancelled.");
        Environment.ExitCode = 2;
    }

    return;
}

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: ExcelRenderer.Http.Sample <workbook.xlsx> <regular-font.ttf> OR --file <workbook.xlsx> <output.pdf> <regular-font.ttf>");
    return;
}

var builder = WebApplication.CreateBuilder();
builder.WebHost.ConfigureKestrel(options => options.AllowSynchronousIO = false);
var app = builder.Build();
PdfHttpEndpoints.Map(app, Path.GetFullPath(args[0]), Path.GetFullPath(args[1]));
await app.RunAsync();
