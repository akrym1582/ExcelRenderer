using System.CommandLine;
using System.CommandLine.Parsing;
using ExcelRenderer.Rendering;

namespace ExcelRenderer.Tool.Commands;

/// <summary>Shared SVG intermediate buffer options for render and svg.</summary>
internal sealed class BufferOptions
{
    private readonly Option<long> threshold = new("--buffer-memory-threshold")
    {
        Description = "SVG intermediate memory threshold in bytes (default: 8388608).",
        DefaultValueFactory = _ => 8 * 1024 * 1024,
    };

    private readonly Option<bool> noTemp = new("--no-buffer-temp") { Description = "Reject SVG buffers exceeding the memory threshold." };
    private readonly Option<string?> directory = new("--buffer-temp-directory") { Description = "Existing directory for SVG intermediate temporary files." };

    /// <summary>Adds the common buffer flags to a CLI command.</summary>
    /// <param name="command">The command used by this operation.</param>
    internal void AddTo(Command command)
    {
        command.Add(threshold);
        command.Add(noTemp);
        command.Add(directory);
    }

    /// <summary>Reads the SVG buffer policy from parsed command arguments.</summary>
    /// <param name="result">The result used by this operation.</param>
    /// <returns>The planned or generated result.</returns>
    internal RenderBufferOptions Get(ParseResult result) => new()
    {
        MemoryThresholdBytes = result.GetValue(threshold),
        AllowTemporaryFiles = !result.GetValue(noTemp),
        TemporaryDirectory = result.GetValue(directory),
    };
}
