namespace ExcelRenderer.Rendering;

/// <summary>Controls seekable intermediate SVG and Skia PDF buffers independently of workbook input buffering.</summary>
public sealed record RenderBufferOptions
{
    /// <summary>Gets the maximum in-memory buffer capacity in bytes.</summary>
    public long MemoryThresholdBytes { get; init; } = 8 * 1024 * 1024;

    /// <summary>Gets a value indicating whether buffers may spill to a temporary file.</summary>
    public bool AllowTemporaryFiles { get; init; } = true;

    /// <summary>Gets an existing temporary directory, or null for the system temporary directory.</summary>
    public string? TemporaryDirectory { get; init; }

    /// <summary>Validates the buffer threshold and the existing temporary directory.</summary>
    internal void Validate() => new Core.Input.InputBufferOptions
    {
        MemoryThresholdBytes = MemoryThresholdBytes,
        AllowTemporaryFiles = AllowTemporaryFiles,
        TemporaryDirectory = TemporaryDirectory,
    }.Validate();
}
