using ExcelRenderer.Core.Abstractions;
using ExcelRenderer.Core.Model;

namespace ExcelRenderer.Core.Layout;

/// <summary>
/// 表示対象の列番号、シート左端からの位置、および列幅を表します。
/// </summary>
internal sealed record ColumnLayout(int Column, double X, double Width);
