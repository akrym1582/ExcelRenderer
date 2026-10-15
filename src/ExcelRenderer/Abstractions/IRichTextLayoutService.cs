using ExcelRenderer.Model;

namespace ExcelRenderer.Abstractions;

/// <summary>Optional capability for finalizing partially formatted cell text.</summary>
public interface IRichTextLayoutService
{
    /// <summary>Finalizes lines and physical font runs without treating style boundaries as breaks.</summary>
    /// <param name="runs">The immutable effective partial formatting.</param>
    /// <param name="font">The cell font used for empty lines.</param>
    /// <param name="availableWidth">The available width in points.</param>
    /// <param name="wrap">Whether to wrap at Unicode text element boundaries.</param>
    /// <returns>The finalized layout.</returns>
    TextLayoutResult Layout(IReadOnlyList<CellTextRun> runs, FontStyle font, double availableWidth, bool wrap);
}
