namespace ExcelRenderer.Fonts;

/// <summary>
/// FontOptions が表すデータと操作を提供します.
/// </summary>
public sealed class FontOptions
{
    /// <summary>
    /// Gets the value. 対応する値を取得または設定します.
    /// </summary>
    public IReadOnlyList<string> FallbackFamilies { get; init; } = ["Noto Sans JP", "Noto Sans", "Liberation Sans"];

    /// <summary>
    /// Gets the value. 対応する値を取得または設定します.
    /// </summary>
    public IReadOnlyList<string> FontDirectories { get; init; } = [];
}
