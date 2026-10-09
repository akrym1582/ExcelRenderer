using ExcelRenderer.Core.Extensibility;
using ExcelRenderer.Model;

namespace ExcelRenderer.CoreIntegration;

/// <summary>Borrows the shape's full style and geometry outside Core.</summary>
/// <param name="Shape">The original shape.</param>
internal sealed record FullShapeData(ReportShape Shape) : ICoreExtensionData;
