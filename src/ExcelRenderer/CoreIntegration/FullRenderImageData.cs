using ExcelRenderer.Core.Extensibility;
using ExcelRenderer.Layout;

namespace ExcelRenderer.CoreIntegration;

/// <summary>Preserves transformed-image and page viewport metadata.</summary>
/// <param name="Image">The placed full image.</param>
internal sealed record FullRenderImageData(RenderImage Image) : ICoreExtensionData;
