using ExcelRenderer.Core;

if (args.Length is < 3 or > 4)
{
    Console.Error.WriteLine("Usage: sample input.xlsx output.pdf font.ttf [sheet-name]");
    return 1;
}

var destination = Path.GetFullPath(args[1]);
var temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
try
{
    ConversionResult result;
    using (var input = File.OpenRead(args[0]))
    using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
    {
        result = await ExcelConverter.ConvertAsync(input, output, new PdfExportOptions
        {
            FontFilePath = args[2],
            SheetName = args.Length == 4 ? args[3] : null,
        });
    }

    File.Move(temporary, destination, overwrite: true);
    Console.WriteLine($"{result.PageCount} pages: {destination}");
    foreach (var diagnostic in result.Diagnostics)
    {
        Console.Error.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");
    }

    return 0;
}
finally
{
    if (File.Exists(temporary))
    {
        File.Delete(temporary);
    }
}
