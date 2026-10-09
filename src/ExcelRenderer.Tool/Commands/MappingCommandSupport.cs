using System.CommandLine;
using System.Text.Json;
using ExcelRenderer.Mapping;

namespace ExcelRenderer.Tool.Commands;

/// <summary>Shares JSON mapping and temporary-input lifetime across CLI commands.</summary>
internal static class MappingCommandSupport
{
    /// <summary>Creates the optional JSON data argument.</summary>
    /// <returns>The data option.</returns>
    internal static Option<string?> DataOption() => new("--data") { Description = "JSON data file to map into the XLSX template before output." };

    /// <summary>Maps JSON to an XLSX file without opening the destination before validation.</summary>
    /// <param name="input">The template path.</param>
    /// <param name="data">The JSON path.</param>
    /// <param name="output">The destination path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The mapping task.</returns>
    internal static async Task MapAsync(string input, string data, string output, CancellationToken cancellationToken)
    {
        CheckPaths(input, data, output);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(data, cancellationToken).ConfigureAwait(false));
        ExcelTemplateMapper.Map(input, output, json.RootElement, cancellationToken: cancellationToken);
    }

    /// <summary>Runs an output operation with an optional mapped temporary workbook.</summary>
    /// <param name="input">The original template path.</param>
    /// <param name="data">The optional JSON path.</param>
    /// <param name="output">The requested output path.</param>
    /// <param name="action">The operation receiving the prepared input path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The output task.</returns>
    internal static async Task WithInputAsync(string input, string? data, string output, Func<string, Task> action, CancellationToken cancellationToken)
    {
        if (data is null)
        {
            await action(input).ConfigureAwait(false);
            return;
        }

        CheckPaths(input, data, output);
        var directory = Path.Combine(Path.GetTempPath(), $"excelrenderer-mapping-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, Path.GetFileName(input));
        try
        {
            await MapAsync(input, data, temporary, cancellationToken).ConfigureAwait(false);
            await action(temporary).ConfigureAwait(false);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static void CheckPaths(string input, string data, string output)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (Path.GetFullPath(input).Equals(Path.GetFullPath(output), comparison) || Path.GetFullPath(data).Equals(Path.GetFullPath(output), comparison))
        {
            throw new ArgumentException("Template, JSON data, and output paths must not overwrite the inputs.");
        }
    }
}
