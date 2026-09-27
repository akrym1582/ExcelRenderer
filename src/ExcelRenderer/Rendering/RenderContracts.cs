using System.Collections.ObjectModel;
using ExcelRenderer.Fonts;

namespace ExcelRenderer.Rendering;

/// <summary>Specifies the representation produced by a render request.</summary>
public enum OutputFormat
{
    Pdf,
    Png,
    Svg,
    Markdown,
}

/// <summary>Specifies the severity of a conversion diagnostic.</summary>
public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>Specifies the pipeline stage that reported a diagnostic.</summary>
public enum DiagnosticStage
{
    Read,
    Layout,
    Render,
    Write,
}

/// <summary>Limits and policies used while preparing the source workbook.</summary>
public sealed record WorkbookInputOptions
{
    public long MemoryThresholdBytes { get; init; } = 16 * 1024 * 1024;
    public long MaxInputBytes { get; init; } = 128 * 1024 * 1024;
    public bool AllowTemporaryFiles { get; init; }
    public string? TemporaryDirectory { get; init; }
    public int MaxZipEntryCount { get; init; } = 10_000;
    public long MaxUncompressedZipBytes { get; init; } = 512 * 1024 * 1024;
}

/// <summary>Selects sheets and rendered pages.</summary>
public sealed record SelectionOptions
{
    public IReadOnlyList<string>? SheetNames { get; init; }
    public IReadOnlyList<int>? Pages { get; init; }
}

/// <summary>Controls collection and failure handling of conversion diagnostics.</summary>
public sealed record DiagnosticOptions
{
    public bool StrictMode { get; init; }
    public IReadOnlyCollection<string> TreatAsErrors { get; init; } = Array.Empty<string>();
    public IReadOnlyCollection<string> SuppressedCodes { get; init; } = Array.Empty<string>();
    public int MaxDiagnostics { get; init; } = 100;
}

/// <summary>Describes a complete stream-based conversion request.</summary>
public sealed record RenderRequest
{
    public OutputFormat OutputFormat { get; init; }
    public SelectionOptions Selection { get; init; } = new();
    public DiagnosticOptions DiagnosticOptions { get; init; } = new();
    public WorkbookInputOptions Input { get; init; } = new();
    /// <summary>Controls font selection for PDF, PNG, and SVG rendering.</summary>
    public FontOptions FontOptions { get; init; } = new();
    public double Dpi { get; init; } = 96;
}

/// <summary>Identifies an artifact before its content is opened.</summary>
public sealed record ArtifactDescriptor(string ArtifactId, string Kind, string MediaType, string RelativeName, int? SourcePageNumber = null, int? OutputPageNumber = null);

/// <summary>Describes a completed conversion artifact.</summary>
public sealed record ArtifactMetadata(ArtifactDescriptor Descriptor, long ByteLength);

/// <summary>Describes the source and output identity of a rendered page.</summary>
public sealed record RenderPageDescriptor(
    int SourceSheetIndex,
    string SourceSheetName,
    int SourcePageNumber,
    int DocumentPageNumber,
    int? OutputPageNumber,
    double WidthPoints,
    double HeightPoints,
    int? PixelWidth = null,
    int? PixelHeight = null,
    double? Dpi = null);

/// <summary>Contains a deduplicated conversion diagnostic.</summary>
public sealed record ConversionDiagnostic(
    string Code,
    DiagnosticSeverity Severity,
    DiagnosticStage Stage,
    string Message,
    string? SheetName = null,
    string? CellRange = null,
    string? ObjectId = null,
    int? SourcePageNumber = null,
    int OccurrenceCount = 1);

/// <summary>Contains the observable result of a conversion.</summary>
public sealed record ConversionResult(
    int SchemaVersion,
    string CompletionStatus,
    IReadOnlyList<string> SelectedSheets,
    IReadOnlyList<RenderPageDescriptor> Pages,
    IReadOnlyList<ArtifactMetadata> Artifacts,
    IReadOnlyList<ConversionDiagnostic> Diagnostics);

/// <summary>Receives artifacts produced by <see cref="ExcelConverter.RenderAsync"/>.</summary>
/// <remarks>
/// The stream returned by <see cref="OpenAsync"/> remains owned by the sink and is never disposed by
/// the renderer. Calls are serialized. A successful open is followed by exactly one complete or abort call.
/// </remarks>
public interface IRenderOutputSink
{
    ValueTask<Stream> OpenAsync(ArtifactDescriptor artifact, CancellationToken cancellationToken);
    ValueTask CompleteAsync(ArtifactDescriptor artifact, long byteLength, CancellationToken cancellationToken);
    ValueTask AbortAsync(ArtifactDescriptor artifact, Exception error, CancellationToken cancellationToken);
}

/// <summary>Raised when a conversion cannot be completed after diagnostics or artifacts were produced.</summary>
public sealed class ConversionException : Exception
{
    public ConversionException(string message, Exception? innerException, IReadOnlyList<ConversionDiagnostic> diagnostics, IReadOnlyList<ArtifactMetadata> artifacts)
        : base(message, innerException)
    {
        CollectedDiagnostics = diagnostics;
        CompletedArtifacts = artifacts;
    }

    public IReadOnlyList<ConversionDiagnostic> CollectedDiagnostics { get; }
    public IReadOnlyList<ArtifactMetadata> CompletedArtifacts { get; }
}
