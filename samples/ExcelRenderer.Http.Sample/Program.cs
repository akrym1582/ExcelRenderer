using ExcelRenderer.Http.Sample;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: ExcelRenderer.Http.Sample <workbook.xlsx> <regular-font.ttf>");
    return;
}

var builder = WebApplication.CreateBuilder();
builder.WebHost.ConfigureKestrel(options => options.AllowSynchronousIO = false);
var app = builder.Build();
PdfHttpEndpoints.Map(app, Path.GetFullPath(args[0]), Path.GetFullPath(args[1]));
await app.RunAsync();
