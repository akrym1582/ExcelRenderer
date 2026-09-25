using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>
/// ページ番号と、そのページに配置するセル、画像、図形、およびヘッダー・フッター文字列を表します。
/// </summary>
public sealed record RenderPage(
    int Number,
    IReadOnlyList<RenderCell> Cells,
    IReadOnlyList<RenderImage>? Images = null,
    IReadOnlyList<RenderText>? HeaderFooterTexts = null,
    IReadOnlyList<RenderShape>? Shapes = null);
