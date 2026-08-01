using ReportEngine.Abstractions;
using ReportEngine.Model;

namespace ReportEngine.Layout;

public sealed record RenderCell(ReportCell Cell, ReportRect Bounds);
