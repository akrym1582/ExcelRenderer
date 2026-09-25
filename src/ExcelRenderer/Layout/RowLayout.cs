using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>
/// RowLayout が表すデータと操作を提供します.
/// </summary>
public sealed record RowLayout(int Row, double Y, double Height);
