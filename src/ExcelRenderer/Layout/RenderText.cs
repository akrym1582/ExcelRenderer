using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>
/// RenderText が表すデータと操作を提供します.
/// </summary>
public sealed record RenderText(ReportRect Bounds, string Text, CellStyle Style);
