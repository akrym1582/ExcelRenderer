using ExcelRenderer.Layout;

namespace ExcelRenderer.Rendering;

/// <summary>A hyperlink resolved against final output pages and coordinates.</summary>
/// <param name="Bounds">The top-origin output rectangle.</param>
/// <param name="Uri">The encoded external URI.</param>
/// <param name="TargetPage">The final one-based target page.</param>
/// <param name="TargetX">The target output X.</param>
/// <param name="TargetY">The target output Y.</param>
/// <param name="TargetHeight">The final target page height.</param>
/// <param name="Tooltip">The optional tooltip.</param>
internal sealed record ResolvedPdfHyperlink(ReportRect Bounds, string? Uri, int? TargetPage, double? TargetX, double? TargetY, double? TargetHeight, string? Tooltip);
