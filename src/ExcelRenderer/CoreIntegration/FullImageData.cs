using ExcelRenderer.Core.Extensibility;
using ExcelRenderer.Model;

namespace ExcelRenderer.CoreIntegration;

/// <summary>Borrows a caller-built image without copying its bytes.</summary>
/// <param name="Image">The original public image.</param>
internal sealed record FullImageData(ReportImage Image) : ICoreExtensionData;
