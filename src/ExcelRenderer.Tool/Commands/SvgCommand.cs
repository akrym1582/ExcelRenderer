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
        var output = CommandSupport.OutputOption("Directory for generated SVG files.");
        var sheet = new Option<string?>("--sheet") { Description = "Worksheet name to convert." };
        var command = new Command("svg", "Convert Excel worksheets to SVG files.") { input, output, sheet };
        command.SetAction((parseResult, cancellationToken) => CommandSupport.RunAsync(() =>
            ExcelConverter.ConvertToSvgAsync(
                parseResult.GetValue(input)!,
                parseResult.GetValue(output)!,
                new SvgExportOptions { SheetName = parseResult.GetValue(sheet) },
                cancellationToken)));
        return command;
    }
}
