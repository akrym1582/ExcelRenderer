using System.CommandLine;
using ExcelRenderer;

namespace ExcelRenderer.Tool.Commands;

/// <summary>Excel ワークシートをページ単位の SVG へ変換するコマンドを構築します。</summary>
public static class SvgCommand
{
    /// <summary>入出力先と対象シートを受け付ける <c>svg</c> コマンドを作成します。</summary>
    /// <returns>指定した Excel ファイルを SVG ファイルへ変換するコマンド。</returns>
    public static Command Create()
    {
        var input = CommandSupport.InputArgument();
        var data = MappingCommandSupport.DataOption();
        var output = CommandSupport.OutputOption("Directory for generated SVG files.");
        var sheet = new Option<string?>("--sheet") { Description = "Worksheet name to convert." };
        var fonts = CommandSupport.FontOptions();
        var buffering = new BufferOptions();
        var command = new Command("svg", "Convert Excel worksheets to SVG files.") { input, output, sheet };
        command.Add(data);
        CommandSupport.AddFontOptions(command, fonts);
        buffering.AddTo(command);
        command.SetAction((parseResult, cancellationToken) => CommandSupport.RunAsync(() => MappingCommandSupport.WithInputAsync(
            parseResult.GetValue(input)!,
            parseResult.GetValue(data),
            parseResult.GetValue(output)!,
            mappedInput => ExcelConverter.ConvertToSvgAsync(
                mappedInput,
                parseResult.GetValue(output)!,
                new SvgExportOptions { Buffering = buffering.Get(parseResult), SheetName = parseResult.GetValue(sheet), FontOptions = CommandSupport.GetFontOptions(parseResult, fonts) },
                cancellationToken),
            cancellationToken)));
        return command;
    }
}
