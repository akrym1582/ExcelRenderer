namespace ExcelRenderer.Rendering;

/// <summary>Maps public input limits to the shared workbook preparer.</summary>
internal static class WorkbookInputPreparer
{
    /// <summary>Reads and validates the input without closing the caller's stream.</summary>
    /// <param name="input">The input at its current position.</param>
    /// <param name="options">The public input limits.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The shared owner of the validated workbook.</returns>
    internal static async Task<PreparedWorkbook> ReadAsync(Stream input, WorkbookInputOptions options, CancellationToken cancellationToken) =>
        new(await Core.Input.WorkbookInputPreparer.ReadAsync(
            input,
            new Core.WorkbookInputOptions
            {
                MaxInputBytes = options.MaxInputBytes,
                MemoryThresholdBytes = options.MemoryThresholdBytes,
                AllowTemporaryFiles = options.AllowTemporaryFiles,
                TemporaryDirectory = options.TemporaryDirectory,
                MaxZipEntryCount = options.MaxZipEntryCount,
                MaxUncompressedZipBytes = options.MaxUncompressedZipBytes,
            },
            cancellationToken).ConfigureAwait(false));
}
