using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ExcelRenderer.OpenXml.Input;

/// <summary>Controls the input spool.</summary>
internal sealed class InputBufferOptions
{
    /// <summary>Gets or sets the maximum in-memory buffer capacity in bytes.</summary>
    public long MemoryThresholdBytes { get; set; } = 16 * 1024 * 1024;

    /// <summary>Gets or sets a value indicating whether buffers may spill to a temporary file.</summary>
    public bool AllowTemporaryFiles { get; set; }

    /// <summary>Gets or sets an existing temporary directory, or null for the system temporary directory.</summary>
    public string? TemporaryDirectory { get; set; }

    /// <summary>Validates the buffer threshold and the existing temporary directory.</summary>
    internal void Validate()
    {
        if (MemoryThresholdBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MemoryThresholdBytes));
        }

        if (TemporaryDirectory is not null && !Directory.Exists(TemporaryDirectory))
        {
            throw new DirectoryNotFoundException($"Temporary directory does not exist: {TemporaryDirectory}");
        }
    }
}
