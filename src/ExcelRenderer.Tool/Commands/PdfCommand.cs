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
        var verbose = CommandSupport.VerboseOption();
        var data = MappingCommandSupport.DataOption();
        var formulaMode = CommandSupport.FormulaModeOption();
        var output = CommandSupport.OutputOption("Path to the output PDF file.");
        var sheet = new Option<string[]>("--sheet") { Description = "Worksheet names in output order (repeatable).", AllowMultipleArgumentsPerToken = true };
        var backend = CommandSupport.PdfBackendOption();
        var hyperlinks = CommandSupport.HyperlinksOption();
        var fonts = CommandSupport.FontOptions();
        var command = new Command("pdf", "Convert Excel worksheets to PDF.") { input, output, sheet };
        command.Add(hyperlinks);
        command.Add(backend);
        command.Add(data);
        command.Add(formulaMode);
        CommandSupport.AddFontOptions(command, fonts);
        command.Add(verbose);
        command.SetAction((parseResult, cancellationToken) => CommandSupport.RunAsync(parseResult, () => MappingCommandSupport.WithInputAsync(
            parseResult.GetValue(input)!,
            parseResult.GetValue(data),
            parseResult.GetValue(output)!,
            mappedInput => ExcelConverter.ConvertToPdfAsync(
                mappedInput,
                parseResult.GetValue(output)!,
                new PdfExportOptions { PdfBackend = CommandSupport.GetPdfBackend(parseResult.GetValue(backend)!), Hyperlinks = CommandSupport.GetHyperlinks(parseResult.GetValue(hyperlinks)!), SheetNames = CommandSupport.GetSheetNames(parseResult.GetValue(sheet)), ReadOptions = CommandSupport.GetReadOptions(parseResult.GetValue(formulaMode)!, parseResult, fonts), FontOptions = CommandSupport.GetFontOptions(parseResult, fonts) },
                cancellationToken),
            cancellationToken,
            CommandSupport.GetMappingOptions(parseResult.GetValue(formulaMode)!))));
        return command;
    }
}
