namespace ExcelRenderer.Fonts;

/// <summary>指定フォントと同梱フォントのどちらを優先して解決するかを指定します。</summary>
public enum FontPolicy
{
    /// <summary>既存の出力互換性のため、同梱の Noto Sans JP を優先します。</summary>
    BundledCompatible,

    /// <summary>明示的に要求または登録されたフォントを優先します。</summary>
    PreferRequested,
}
