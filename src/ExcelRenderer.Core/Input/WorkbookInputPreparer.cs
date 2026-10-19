namespace ExcelRenderer.Core.Input;

/// <summary>Adapts Core input options to the shared bounded OOXML preparation.</summary>
internal static class WorkbookInputPreparer
{
    /// <summary>Prepares a borrowed stream without changing its ownership.</summary>
    /// <param name="input">Input at its current position.</param>
    /// <param name="options">The existing Core limits.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>The owner of validated seekable storage.</returns>
    internal static async Task<PreparedWorkbook> ReadAsync(Stream input, WorkbookInputOptions options, CancellationToken cancellationToken) =>
        new(await OpenXml.Reader.Input.WorkbookInputPreparer.ReadAsync(
            input,
            new OpenXml.Reader.Input.WorkbookInputOptions
            {
                MemoryThresholdBytes = options.MemoryThresholdBytes,
                MaxInputBytes = options.MaxInputBytes,
                AllowTemporaryFiles = options.AllowTemporaryFiles,
                TemporaryDirectory = options.TemporaryDirectory,
                MaxZipEntryCount = options.MaxZipEntryCount,
                MaxUncompressedZipBytes = options.MaxUncompressedZipBytes,
            },
            cancellationToken,
            () => Rendering.ConversionMetrics.Report("buffer.spillCount", 1)).ConfigureAwait(false));
}
