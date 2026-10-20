using System.CommandLine;
using ExcelRenderer;

namespace ExcelRenderer.Tool.Commands;

/// <summary>
/// Excel ワークシートを PostScript 文書へ変換するコマンドを構築します。
/// </summary>
public static class PostScriptCommand
{
    /// <summary>
    /// 入出力先と対象シートを受け付ける <c>ps</c> コマンドを作成します。
    /// </summary>
    /// <returns>指定した Excel ファイルのワークシートを PostScript ファイルへ変換するコマンド。</returns>
    public static Command Create()
    {
        var input = CommandSupport.InputArgument();
        var data = MappingCommandSupport.DataOption();
        var formulaMode = CommandSupport.FormulaModeOption();
        var output = CommandSupport.OutputOption("Path to the output PostScript file.");
        var sheet = new Option<string?>("--sheet") { Description = "Worksheet name to convert." };
        var fonts = CommandSupport.FontOptions();
        var buffering = new BufferOptions();
        var command = new Command("ps", "Convert Excel worksheets to PostScript.") { input, output, sheet };
        command.Add(data);
        command.Add(formulaMode);
        CommandSupport.AddFontOptions(command, fonts);
        buffering.AddTo(command);
        command.SetAction((parseResult, cancellationToken) => CommandSupport.RunAsync(() => MappingCommandSupport.WithInputAsync(
            parseResult.GetValue(input)!,
            parseResult.GetValue(data),
            parseResult.GetValue(output)!,
            mappedInput => ExcelConverter.ConvertToPostScriptAsync(
                mappedInput,
                parseResult.GetValue(output)!,
                new PostScriptExportOptions { Buffering = buffering.Get(parseResult), SheetName = parseResult.GetValue(sheet), ReadOptions = CommandSupport.GetReadOptions(parseResult.GetValue(formulaMode)!, parseResult, fonts), FontOptions = CommandSupport.GetFontOptions(parseResult, fonts) },
                cancellationToken),
            cancellationToken,
            CommandSupport.GetMappingOptions(parseResult.GetValue(formulaMode)!))));
        return command;
    }
}
