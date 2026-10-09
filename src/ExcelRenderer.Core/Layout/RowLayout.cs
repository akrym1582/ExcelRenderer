using ExcelRenderer.Core.Abstractions;
using ExcelRenderer.Core.Model;

namespace ExcelRenderer.Core.Layout;

/// <summary>
/// 表示対象の行番号、シート上端からの位置、および行高を表します。
/// </summary>
internal sealed record RowLayout(int Row, double Y, double Height);
