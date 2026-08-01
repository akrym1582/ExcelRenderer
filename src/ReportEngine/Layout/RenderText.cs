using ReportEngine.Abstractions;
using ReportEngine.Model;

namespace ReportEngine.Layout;

public sealed record RenderText(ReportRect Bounds, string Text, CellStyle Style);
