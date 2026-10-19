namespace ExcelRenderer.Fonts;

/// <summary>明示的に登録するフォントフェイスを表します。</summary>
public sealed record FontRegistration(
    string Family,
    string Regular,
    string? Bold = null,
    string? Italic = null,
    string? BoldItalic = null)
{
    /// <summary>Gets the zero-based collection index for the regular face.</summary>
    public int RegularCollectionIndex { get; init; }

    /// <summary>Gets the zero-based collection index for the bold face.</summary>
    public int BoldCollectionIndex { get; init; }

    /// <summary>Gets the zero-based collection index for the italic face.</summary>
    public int ItalicCollectionIndex { get; init; }

    /// <summary>Gets the zero-based collection index for the bold italic face.</summary>
    public int BoldItalicCollectionIndex { get; init; }
}
