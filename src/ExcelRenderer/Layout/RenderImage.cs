using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

public sealed record RenderImage(ReportRect Bounds, byte[] ImageBytes, int ZIndex = 0);
