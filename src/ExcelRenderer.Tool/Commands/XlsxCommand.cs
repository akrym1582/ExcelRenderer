using System.CommandLine;

namespace ExcelRenderer.Tool.Commands;

/// <summary>Builds the template-mapping-only XLSX command.</summary>
public static class XlsxCommand
{
    /// <summary>Creates the xlsx command.</summary>
    /// <returns>The command requiring a template, JSON data and an output path.</returns>
    public static Command Create()
    {
        var input = CommandSupport.InputArgument();
        var output = CommandSupport.OutputOption("Path to the mapped XLSX file.");
        var data = MappingCommandSupport.DataOption();
        data.Required = true;
        var command = new Command("xlsx", "Map JSON into an Excel template and save XLSX without rendering.") { input, output, data };
        command.SetAction((result, cancellationToken) => CommandSupport.RunAsync(() => MappingCommandSupport.MapAsync(
            result.GetValue(input)!,
            result.GetValue(data)!,
            result.GetValue(output)!,
            cancellationToken)));
        return command;
    }
}
