using ExcelRenderer.Slim.Abstractions;
using ExcelRenderer.Slim.Model;

namespace ExcelRenderer.Slim.Layout;

/// <summary>
/// 表示対象の列番号、シート左端からの位置、および列幅を表します。
/// </summary>
internal sealed record ColumnLayout(int Column, double X, double Width);
