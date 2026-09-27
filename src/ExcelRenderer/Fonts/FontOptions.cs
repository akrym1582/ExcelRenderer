namespace ExcelRenderer.Fonts;

/// <summary>フォント解決時のフォールバック順序と検索場所を指定します。</summary>
public sealed class FontOptions
{
    /// <summary>Gets the font selection policy. フォント選択時の優先順位を取得します。</summary>
    public FontPolicy Policy { get; init; } = FontPolicy.BundledCompatible;

    /// <summary>Gets whether operating-system font directories may be searched.</summary>
    public bool AllowSystemFonts { get; init; } = true;

    /// <summary>Gets the fallback font families. 要求されたフォントが見つからない場合に、記載順で検索するフォントファミリー名を取得します。</summary>
    public IReadOnlyList<string> FallbackFamilies { get; init; } = ["Noto Sans JP", "Noto Sans", "Liberation Sans"];

    /// <summary>Gets the additional font directories. オペレーティングシステム標準の場所に加えて再帰的に検索するフォントディレクトリを取得します。</summary>
    public IReadOnlyList<string> FontDirectories { get; init; } = [];

    /// <summary>Gets explicitly registered font families. 明示的に登録するフォントフェイスを取得します。</summary>
    public IReadOnlyList<FontRegistration> Registrations { get; init; } = [];
}
