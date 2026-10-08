using ExcelRenderer.Slim.Abstractions;
using ExcelRenderer.Slim.Model;

namespace ExcelRenderer.Slim.Layout;

/// <summary>
/// ページ番号と、そのページに配置するセル、画像、およびヘッダー・フッター文字列を表します。
/// </summary>
internal sealed record RenderPage(
    int Number,
    IReadOnlyList<RenderCell> Cells,
    IReadOnlyList<RenderImage>? Images = null,
    IReadOnlyList<RenderText>? HeaderFooterTexts = null);
