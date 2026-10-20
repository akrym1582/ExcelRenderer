using ExcelRenderer.Core.Rendering;
using ExcelRenderer.OpenXml.Reader.Input;

namespace ExcelRenderer.Core.Input;

/// <summary>Creates shared buffers with conversion-local spill metrics.</summary>
internal static class InputBufferFactory
{
    /// <summary>Creates a buffer with the shared storage policy.</summary>
    /// <param name="options">The buffer settings.</param>
    /// <returns>The owned shared buffer.</returns>
    internal static SpillableBufferStream Create(InputBufferOptions options) =>
        new(options, () => ConversionMetrics.Report("buffer.spillCount", 1));
}
