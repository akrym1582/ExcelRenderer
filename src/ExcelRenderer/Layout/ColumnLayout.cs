using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>
/// 表示対象の列番号、シート左端からの位置、および列幅を表します。
/// </summary>
public sealed record ColumnLayout(int Column, double X, double Width);
