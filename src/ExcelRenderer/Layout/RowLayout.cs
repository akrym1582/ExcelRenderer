using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>
/// 表示対象の行番号、シート上端からの位置、および行高を表します。
/// </summary>
public sealed record RowLayout(int Row, double Y, double Height);
