using ClosedXML.Excel;
using ExcelRenderer.OpenXml;

namespace ExcelRenderer.Core.Excel;

/// <summary>Original XML result presence, typed value and unexpanded formula metadata.</summary>
/// <param name="HasValue">Whether a saved result element was present, including an empty string.</param>
/// <param name="Value">The original typed result, independent of a loaded formula cell.</param>
/// <param name="IsError">Whether the saved result is an Excel error.</param>
/// <param name="Formula">Original formula metadata, including shared and array followers.</param>
internal sealed record FormulaCache(bool HasValue, XLCellValue Value, bool IsError, FormulaInfo Formula);
