namespace ExcelRenderer.Fonts;

/// <summary>明示的に登録するフォントフェイスを表します。</summary>
public sealed record FontRegistration(
    string Family,
    string Regular,
    string? Bold = null,
    string? Italic = null,
    string? BoldItalic = null);
