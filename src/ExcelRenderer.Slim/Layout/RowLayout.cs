using ExcelRenderer.Slim.Abstractions;
using ExcelRenderer.Slim.Model;

namespace ExcelRenderer.Slim.Layout;

/// <summary>
/// 表示対象の行番号、シート上端からの位置、および行高を表します。
/// </summary>
internal sealed record RowLayout(int Row, double Y, double Height);
