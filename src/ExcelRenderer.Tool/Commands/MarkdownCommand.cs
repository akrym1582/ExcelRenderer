using ExcelRenderer.Markdown;
using System.CommandLine;
using ExcelRenderer;

namespace ExcelRenderer.Tool.Commands;

public static class MarkdownCommand
{
    public static Command Create()
    {
        var input = CommandSupport.InputArgument();
        var output = CommandSupport.OutputOption("Path to the output Markdown file.");
        var sheet = new Option<string?>("--sheet") { Description = "Worksheet name to convert." };
        var images = Flag("--images", "Export embedded images.");
        var noImages = Flag("--no-images", "Do not export embedded images.");
        var addresses = Flag("--cell-addresses", "Include cell addresses.");
        var noAddresses = Flag("--no-cell-addresses", "Do not include cell addresses.");
        var formulas = Flag("--formulas", "Include formulas.");
        var noFormulas = Flag("--no-formulas", "Do not include formulas.");
        var layout = Flag("--layout-detection", "Enable layout detection.");
        var noLayout = Flag("--no-layout-detection", "Disable layout detection.");
        var regions = Flag("--region-detection", "Enable region detection.");
        var noRegions = Flag("--no-region-detection", "Disable region detection.");
        var imageDir = new Option<string>("--image-dir") { Description = "Relative image directory name.", DefaultValueFactory = _ => "images" };
        var command = new Command("markdown", "Convert an Excel workbook to Markdown.")
            { input, output, sheet, images, noImages, addresses, noAddresses, formulas, noFormulas,
                layout, noLayout, regions, noRegions, imageDir };
        command.Aliases.Add("md");
        command.SetAction((result, cancellationToken) => CommandSupport.RunAsync(() =>
            ExcelConverter.ConvertToMarkdownAsync(result.GetValue(input)!, result.GetValue(output)!,
                new MarkdownExportOptions
                {
                    SheetName = result.GetValue(sheet),
                    ExportImages = Enabled(result, images, noImages, true),
                    IncludeCellAddresses = Enabled(result, addresses, noAddresses, true),
                    IncludeFormula = Enabled(result, formulas, noFormulas, true),
                    DetectLayout = Enabled(result, layout, noLayout, true),
                    DetectRegions = Enabled(result, regions, noRegions, true),
                    ImageDirectoryName = result.GetValue(imageDir)!
                }, cancellationToken)));
        return command;
    }

    private static Option<bool> Flag(string name, string description) => new(name) { Description = description };

    private static bool Enabled(ParseResult result, Option<bool> yes, Option<bool> no, bool defaultValue) =>
        result.GetValue(no) ? false : result.GetValue(yes) ? true : defaultValue;
}
