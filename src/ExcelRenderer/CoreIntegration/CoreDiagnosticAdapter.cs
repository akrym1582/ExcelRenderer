using ExcelRenderer.Rendering;

namespace ExcelRenderer.CoreIntegration;

/// <summary>Maps shared diagnostics without losing their source context.</summary>
internal static class CoreDiagnosticAdapter
{
    /// <summary>Maps a shared diagnostic to the existing public record.</summary>
    /// <param name="value">The shared diagnostic.</param>
    /// <returns>The public diagnostic.</returns>
    internal static ConversionDiagnostic ToPublic(Core.Rendering.ConversionDiagnostic value) => new(
        value.Code,
        ToPublic(value.Severity),
        ToPublic(value.Stage),
        value.Message,
        value.SheetName,
        value.CellRange,
        value.ObjectId,
        value.SourcePageNumber,
        value.OccurrenceCount);

    private static DiagnosticSeverity ToPublic(Core.Rendering.DiagnosticSeverity value) => value switch
        {
            Core.Rendering.DiagnosticSeverity.Info => DiagnosticSeverity.Info,
            Core.Rendering.DiagnosticSeverity.Warning => DiagnosticSeverity.Warning,
            Core.Rendering.DiagnosticSeverity.Error => DiagnosticSeverity.Error,
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };

    private static DiagnosticStage ToPublic(Core.Rendering.DiagnosticStage value) => value switch
        {
            Core.Rendering.DiagnosticStage.Read => DiagnosticStage.Read,
            Core.Rendering.DiagnosticStage.Layout => DiagnosticStage.Layout,
            Core.Rendering.DiagnosticStage.Render => DiagnosticStage.Render,
            Core.Rendering.DiagnosticStage.Write => DiagnosticStage.Write,
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };
}
