using ReportEngine.Abstractions;
using ReportEngine.Model;

namespace ReportEngine.Layout;

public sealed record RenderImage(ReportRect Bounds, byte[] ImageBytes);
