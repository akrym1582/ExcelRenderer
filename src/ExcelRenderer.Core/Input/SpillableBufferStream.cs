using ExcelRenderer.Core.Rendering;

namespace ExcelRenderer.Core.Input;

/// <summary>Adapts Core buffering settings and metrics to the shared spool.</summary>
internal class SpillableBufferStream : OpenXml.Input.SpillableBufferStream
{
    /// <summary>Initializes a new instance of the <see cref="SpillableBufferStream"/> class.</summary>
    /// <param name="options">The Core buffer settings.</param>
    internal SpillableBufferStream(InputBufferOptions options)
        : base(options.ToShared(), () => ConversionMetrics.Report("buffer.spillCount", 1))
    {
    }

    /// <summary>Gets a value indicating whether storage has spilled to disk.</summary>
    internal new bool HasSpilled => base.HasSpilled;

    /// <summary>Gets the current allocated memory capacity.</summary>
    internal new int MemoryCapacity => base.MemoryCapacity;

    /// <summary>Gets the peak allocated memory capacity.</summary>
    internal new int PeakMemoryCapacity => base.PeakMemoryCapacity;

    /// <summary>Opens an independent cursor over the owned storage.</summary>
    /// <returns>A borrowed read stream.</returns>
    internal new Stream OpenRead() => base.OpenRead();
}
