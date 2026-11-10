using System.Collections.Generic;
using ExcelRenderer.OpenXml.Reader.Diagnostics;

namespace ExcelRenderer.OpenXml.Reader.Formatting;

/// <summary>Immutable CellDisplayResult snapshot. XML attribute names and source units are preserved.</summary>
public sealed class CellDisplayResult
{
    /// <summary>Initializes a new instance of the <see cref="CellDisplayResult"/> class.</summary>
    /// <param name="text">The text value.</param>
    /// <param name="foregroundRgb">Selected named number-format color, or null.</param>
    /// <param name="diagnostics">The diagnostics value.</param>
    internal CellDisplayResult(string text, IReadOnlyList<ReaderDiagnostic> diagnostics, string? foregroundRgb = null)
    {
        Text = text;
        ForegroundRgb = foregroundRgb;
        Diagnostics = diagnostics;
    }

    /// <summary>Gets the selected number-format foreground as six RGB hex digits, or null.</summary>
    public string? ForegroundRgb { get; }

    /// <summary>Gets the Text metadata.</summary>
    public string Text { get; }

    /// <summary>Gets the Diagnostics metadata.</summary>
    public IReadOnlyList<ReaderDiagnostic> Diagnostics { get; }
}
