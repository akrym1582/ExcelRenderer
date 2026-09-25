using System.CommandLine;
using ExcelRenderer;

namespace ExcelRenderer.Tool.Commands;

/// <summary>
/// PdfCommand が表すデータと操作を提供します.
/// </summary>
public static class PdfCommand
{
    /// <summary>
    /// Create を実行します.
    /// </summary>
    /// <returns>処理によって得られた結果を返します。</returns>
    public static Command Create()
    {
        var input = CommandSupport.InputArgument();
        var output = CommandSupport.OutputOption("Path to the output PDF file.");
        var sheet = new Option<string?>("--sheet") { Description = "Worksheet name to convert." };
        var command = new Command("pdf", "Convert Excel worksheets to PDF.") { input, output, sheet };
        command.SetAction((parseResult, cancellationToken) => CommandSupport.RunAsync(() =>
            ExcelConverter.ConvertToPdfAsync(
                parseResult.GetValue(input)!,
                parseResult.GetValue(output)!,
                new PdfExportOptions { SheetName = parseResult.GetValue(sheet) },
                cancellationToken)));
        return command;
    }
}
