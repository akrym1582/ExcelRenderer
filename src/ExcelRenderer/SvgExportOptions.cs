using ExcelRenderer.Fonts;

namespace ExcelRenderer;

/// <summary>SVG 出力の対象ワークシートを指定します。</summary>
public sealed class SvgExportOptions
{
    /// <summary>Gets the worksheet name to export, or null to export all worksheets.</summary>
    public string? SheetName { get; init; }

    /// <summary>Gets the font selection criteria. 描画に使用するフォントの検索設定を取得します。</summary>
    public FontOptions FontOptions { get; init; } = new();
}
