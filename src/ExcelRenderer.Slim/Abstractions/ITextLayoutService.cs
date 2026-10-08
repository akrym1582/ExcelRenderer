using ExcelRenderer.Slim.Model;

namespace ExcelRenderer.Slim.Abstractions;

/// <summary>Creates a reusable text layout consumed without renderer-side line breaking.</summary>
internal interface ITextLayoutService
{
    /// <summary>Creates positioned lines and resolved font runs for the supplied content width.</summary>
    /// <param name="text">Text to lay out.</param>
    /// <param name="font">Requested font style.</param>
    /// <param name="availableWidth">Available width in points.</param>
    /// <param name="wrap">Whether automatic wrapping is enabled.</param>
    /// <returns>The finalized text layout.</returns>
    TextLayoutResult Layout(string text, FontStyle font, double availableWidth, bool wrap);
}
