using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>
/// RenderPage が表すデータと操作を提供します.
/// </summary>
public sealed record RenderPage(
    int Number,
    IReadOnlyList<RenderCell> Cells,
    IReadOnlyList<RenderImage>? Images = null,
    IReadOnlyList<RenderText>? HeaderFooterTexts = null,
    IReadOnlyList<RenderShape>? Shapes = null);
