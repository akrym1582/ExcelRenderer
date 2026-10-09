using ExcelRenderer.Abstractions;
using ExcelRenderer.Core.Extensibility;

namespace ExcelRenderer.CoreIntegration;

/// <summary>Borrows immutable measured layout without retaining a page or native owner.</summary>
/// <param name="Layout">The original source layout.</param>
internal sealed record FullTextLayoutData(TextLayoutResult Layout) : ICoreExtensionData;
