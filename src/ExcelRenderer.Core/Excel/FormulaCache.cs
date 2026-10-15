using ClosedXML.Excel;

namespace ExcelRenderer.Core.Excel;

/// <summary>Original XML result presence and an unevaluated typed snapshot.</summary>
/// <param name="HasValue">Whether a saved result element was present, including an empty string.</param>
/// <param name="Type">The original XML result type.</param>
/// <param name="RawValue">The original XML result text.</param>
/// <param name="Value">ClosedXML's typed, date-system-aware loaded value.</param>
internal sealed record FormulaCache(bool HasValue, string? Type, string? RawValue, XLCellValue Value);
