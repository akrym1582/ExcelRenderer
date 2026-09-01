using System.CommandLine;

namespace ExcelRenderer.Tool.Commands;

public static class ImageCommand
{
    public static Command Create()
    {
        var input = CommandSupport.InputArgument();
        var output = CommandSupport.OutputOption("Directory for generated PNG files.");
        var sheet = new Option<string?>("--sheet") { Description = "Worksheet name to convert." };
        var dpi = new Option<int>("--dpi") { Description = "Output resolution in dots per inch.", DefaultValueFactory = _ => 144 };
        dpi.Validators.Add(result =>
        {
            if (result.GetValueOrDefault<int>() <= 0) result.AddError("--dpi must be greater than zero.");
        });
        var command = new Command("image", "Convert Excel worksheets to PNG images.") { input, output, sheet, dpi };
        command.SetAction((parseResult, cancellationToken) => CommandSupport.RunAsync(() =>
            ExcelConverter.ConvertToImagesAsync(parseResult.GetValue(input)!, parseResult.GetValue(output)!,
                new ImageExportOptions { SheetName = parseResult.GetValue(sheet), Dpi = parseResult.GetValue(dpi) }, cancellationToken)));
        return command;
    }
}
