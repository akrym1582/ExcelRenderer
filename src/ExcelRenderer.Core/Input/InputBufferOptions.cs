namespace ExcelRenderer.Core.Input;

/// <summary>Controls the input spool.</summary>
internal sealed record InputBufferOptions
{
    /// <summary>Gets the maximum in-memory buffer capacity in bytes.</summary>
    public long MemoryThresholdBytes { get; init; } = 16 * 1024 * 1024;

    /// <summary>Gets a value indicating whether buffers may spill to a temporary file.</summary>
    public bool AllowTemporaryFiles { get; init; }

    /// <summary>Gets an existing temporary directory, or null for the system temporary directory.</summary>
    public string? TemporaryDirectory { get; init; }

    /// <summary>Validates the buffer threshold and the existing temporary directory.</summary>
    internal void Validate() => ToShared().Validate();

    /// <summary>Adapts values to the generic spool owner.</summary>
    /// <returns>The shared buffer settings.</returns>
    internal OpenXml.Input.InputBufferOptions ToShared() => new()
    {
        MemoryThresholdBytes = MemoryThresholdBytes,
        AllowTemporaryFiles = AllowTemporaryFiles,
        TemporaryDirectory = TemporaryDirectory,
    };
}
