using ExcelRenderer.Core.Extensibility;
using ExcelRenderer.Excel;

namespace ExcelRenderer.CoreIntegration;

/// <summary>Retains transformations without changing Core's simple image policy.</summary>
/// <param name="Metadata">Original transformation metadata.</param>
internal sealed record FullPictureData(DrawingPictureMetadata Metadata) : ICoreExtensionData;
