namespace ExcelRenderer.Rendering;

/// <summary>Adapts public output buffering settings to the shared spool.</summary>
internal sealed class SpillableBufferStream : Core.Input.SpillableBufferStream
{
    /// <summary>Initializes a new instance of the <see cref="SpillableBufferStream"/> class.</summary>
    /// <param name="options">The public buffering settings.</param>
    internal SpillableBufferStream(RenderBufferOptions options)
        : base(new Core.Input.InputBufferOptions
        {
            MemoryThresholdBytes = options.MemoryThresholdBytes,
            AllowTemporaryFiles = options.AllowTemporaryFiles,
            TemporaryDirectory = options.TemporaryDirectory,
        })
    {
    }
}
