using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

public sealed record RenderText(ReportRect Bounds, string Text, CellStyle Style);
