using System.Collections.Generic;

namespace ExcelRenderer.OpenXml;

/// <summary>Immutable CellDisplayResult snapshot. XML attribute names and source units are preserved.</summary>
public sealed class CellDisplayResult
{
    /// <summary>Initializes a new instance of the <see cref="CellDisplayResult"/> class.</summary>
    /// <param name="text">The text value.</param>
    /// <param name="diagnostics">The diagnostics value.</param>
    internal CellDisplayResult(string text, IReadOnlyList<ReaderDiagnostic> diagnostics)
    {
        Text = text;
        Diagnostics = diagnostics;
    }

    /// <summary>Gets the Text metadata.</summary>
    public string Text { get; }

    /// <summary>Gets the Diagnostics metadata.</summary>
    public IReadOnlyList<ReaderDiagnostic> Diagnostics { get; }
}
