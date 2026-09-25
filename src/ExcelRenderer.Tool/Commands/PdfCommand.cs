using System.CommandLine;
using ExcelRenderer;

namespace ExcelRenderer.Tool.Commands;

/// <summary>
/// Excel ワークシートを PDF 文書へ変換するコマンドを構築します。
/// </summary>
public static class PdfCommand
{
    /// <summary>
    /// 入出力先と対象シートを受け付ける <c>pdf</c> コマンドを作成します。
    /// </summary>
    /// <returns>指定した Excel ファイルのワークシートを PDF ファイルへ変換するコマンド。</returns>
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
