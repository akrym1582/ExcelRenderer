using ExcelRenderer.Core.Extensibility;
using ExcelRenderer.Excel;
using ExcelRenderer.Model;

namespace ExcelRenderer.CoreIntegration;

/// <summary>Retains full sheet features alongside the common body model.</summary>
/// <param name="Shapes">Selected-sheet shapes.</param>
/// <param name="Hyperlinks">Original hyperlink definitions and display metadata.</param>
internal sealed record FullSheetData(IReadOnlyList<ReportShape> Shapes, SheetHyperlinkMetadata? Hyperlinks) : ICoreExtensionData;
