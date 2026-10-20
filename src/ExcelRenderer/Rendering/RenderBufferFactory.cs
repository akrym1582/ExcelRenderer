using ExcelRenderer.OpenXml.Reader.Input;

namespace ExcelRenderer.Rendering;

/// <summary>Maps public output settings and creates shared buffers with spill metrics.</summary>
internal static class RenderBufferFactory
{
    /// <summary>Creates an output buffer using a copy of the public settings.</summary>
    /// <param name="options">The public buffering settings.</param>
    /// <returns>The owned shared buffer.</returns>
    internal static SpillableBufferStream Create(RenderBufferOptions options) =>
        Core.Input.InputBufferFactory.Create(ToInputOptions(options));

    /// <summary>Copies the public settings to the shared spool policy.</summary>
    /// <param name="options">The public buffering settings.</param>
    /// <returns>The shared buffer settings.</returns>
    internal static InputBufferOptions ToInputOptions(RenderBufferOptions options) => new()
    {
        MemoryThresholdBytes = options.MemoryThresholdBytes,
        AllowTemporaryFiles = options.AllowTemporaryFiles,
        TemporaryDirectory = options.TemporaryDirectory,
    };
}
