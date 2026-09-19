using System.CommandLine;
using ExcelRenderer;

namespace ExcelRenderer.Tool.Commands;

public static class PdfCommand
{
    public static Command Create()
    {
        var input = CommandSupport.InputArgument();
        var output = CommandSupport.OutputOption("Path to the output PDF file.");
        var sheet = new Option<string?>("--sheet") { Description = "Worksheet name to convert." };
        var command = new Command("pdf", "Convert Excel worksheets to PDF.") { input, output, sheet };
        command.SetAction((parseResult, cancellationToken) => CommandSupport.RunAsync(() =>
            ExcelConverter.ConvertToPdfAsync(parseResult.GetValue(input)!, parseResult.GetValue(output)!,
                new PdfExportOptions { SheetName = parseResult.GetValue(sheet) }, cancellationToken)));
        return command;
    }
}
