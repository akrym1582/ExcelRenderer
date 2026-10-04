using ExcelRenderer.Layout;

namespace ExcelRenderer.Drawing;

/// <summary>Applies a shared clip and translation to already finalized commands.</summary>
/// <param name="PageNumber">The page number.</param>
/// <param name="Commands">The original drawing commands.</param>
/// <param name="Clip">The original page-space clipping rectangle.</param>
/// <param name="OffsetX">The horizontal output translation.</param>
/// <param name="OffsetY">The vertical output translation.</param>
internal sealed record DrawViewportCommand(
    int PageNumber,
    IReadOnlyList<DrawCommand> Commands,
    ReportRect Clip,
    double OffsetX,
    double OffsetY) : DrawCommand(PageNumber);
