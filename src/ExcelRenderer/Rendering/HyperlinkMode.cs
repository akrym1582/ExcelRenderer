namespace ExcelRenderer.Rendering;

/// <summary>Controls preservation of cell hyperlinks in interactive output.</summary>
public enum HyperlinkMode
{
    /// <summary>Preserve supported hyperlinks.</summary>
    Preserve,

    /// <summary>Keep display text without hyperlinks or hyperlink diagnostics.</summary>
    None,
}
