using ExcelRenderer.Sample;

if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: ExcelRenderer.Sample <workbook.xlsx> <output.pdf> <regular-font.ttf>");
    return 1;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};
try
{
    var result = await PdfFileExample.ConvertAsync(args[0], args[1], args[2], cancellation.Token);
    Console.WriteLine($"{result.Pages.Count} pages: {args[1]}");
    foreach (var diagnostic in result.Diagnostics)
    {
        Console.Error.WriteLine($"{diagnostic.Code}: sheet={diagnostic.SheetName} page={diagnostic.SourcePageNumber} count={diagnostic.OccurrenceCount}");
    }

    return 0;
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    Console.Error.WriteLine("Conversion cancelled.");
    return 2;
}
