namespace ExcelRenderer.Fonts;

/// <summary>フォント解決時のフォールバック順序と検索場所を指定します。</summary>
public sealed class FontOptions
{
    /// <summary>Gets the font selection policy. フォント選択時に適用するポリシーを取得または設定します。</summary>
    public FontPolicy Policy { get; init; } = FontPolicy.BundledCompatible;

    /// <summary>Gets the bundled Gothic or Mincho face preferred for registered IVS glyphs. IVS 字形に優先する同梱のゴシック体または明朝体を取得または設定します。</summary>
    public IvsFontStyle IvsFontStyle { get; init; } = IvsFontStyle.Gothic;

    /// <summary>Gets a value indicating whether operating-system font directories may be searched. OS 標準のフォントフォルダーを検索するかどうかを取得または設定します。</summary>
    public bool AllowSystemFonts { get; init; } = true;

    /// <summary>Gets the fallback font families. フォールバックとして記載順に検索するフォントファミリー名を取得または設定します。要求されたフォントが見つからない場合に使用します。</summary>
    public IReadOnlyList<string> FallbackFamilies { get; init; } = ["Noto Sans JP", "Noto Sans", "Liberation Sans"];

    /// <summary>Gets the additional font directories. OS 標準の場所に加えて再帰的に検索するフォントディレクトリを取得または設定します。</summary>
    public IReadOnlyList<string> FontDirectories { get; init; } = [];

    /// <summary>Gets explicitly registered font faces. 明示的に登録するフォントフェイスの一覧を取得または設定します。</summary>
    public IReadOnlyList<FontRegistration> Registrations { get; init; } = [];
}
