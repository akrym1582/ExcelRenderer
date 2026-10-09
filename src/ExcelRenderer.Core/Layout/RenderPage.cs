using ExcelRenderer.Core.Abstractions;
using ExcelRenderer.Core.Model;

namespace ExcelRenderer.Core.Layout;

/// <summary>
/// ページ番号と、そのページに配置するセル、画像、およびヘッダー・フッター文字列を表します。
/// </summary>
internal sealed record RenderPage(
    int Number,
    IReadOnlyList<RenderCell> Cells,
    IReadOnlyList<RenderImage>? Images = null,
    IReadOnlyList<RenderText>? HeaderFooterTexts = null)
{
    /// <summary>Gets borrowed product page metadata.</summary>
    internal Extensibility.ICoreExtensionData? ExtensionData { get; init; }
}
