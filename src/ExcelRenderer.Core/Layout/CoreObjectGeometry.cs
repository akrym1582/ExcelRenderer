using ExcelRenderer.Core.Extensibility;
using ExcelRenderer.Core.Model;

namespace ExcelRenderer.Core.Layout;

/// <summary>Supplies neutral object rectangles before range resolution and pagination.</summary>
/// <param name="Anchor">The range anchor, including resolved absolute anchors.</param>
/// <param name="Bounds">The original untransformed rectangle.</param>
/// <param name="Visual">The range-defining visual rectangle.</param>
/// <param name="Image">The basic image, or null for a product object.</param>
/// <param name="ExtensionData">Borrowed product object metadata.</param>
internal sealed record CoreObjectGeometry(CellAddress Anchor, ReportRect Bounds, ReportRect Visual, ReportImage? Image, ICoreExtensionData? ExtensionData = null);
