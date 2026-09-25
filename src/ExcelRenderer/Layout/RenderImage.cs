using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>
/// RenderImage が表すデータと操作を提供します.
/// </summary>
public sealed record RenderImage(ReportRect Bounds, byte[] ImageBytes, int ZIndex = 0);
