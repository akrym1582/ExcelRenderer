using ReportEngine.Abstractions;
using ReportEngine.Model;

namespace ReportEngine.Layout;

public sealed record CellLayout(CellAddress Address, ReportRect Bounds, TextSize TextSize);
