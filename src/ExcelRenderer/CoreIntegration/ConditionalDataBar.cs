using ExcelRenderer.Core.Model;

namespace ExcelRenderer.CoreIntegration;

/// <summary>Normalized bar interval and resolved colors.</summary>
internal sealed record ConditionalDataBar(double Start, double End, double? Axis, ReportColor Fill, ReportColor? Border, ReportColor? AxisColor, bool ShowValue);
