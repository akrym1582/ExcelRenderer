using ExcelRenderer.Core.Extensibility;
using ExcelRenderer.Layout;

namespace ExcelRenderer.CoreIntegration;

/// <summary>Borrows a single page's full content and source mappings.</summary>
/// <param name="Shapes">The page-local shapes.</param>
/// <param name="Regions">The source region mappings.</param>
internal sealed record FullPageData(IReadOnlyList<RenderShape> Shapes, IReadOnlyList<PageSourceRegion> Regions) : ICoreExtensionData;
