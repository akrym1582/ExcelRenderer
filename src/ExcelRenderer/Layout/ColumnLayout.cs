using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>
/// ColumnLayout が表すデータと操作を提供します.
/// </summary>
public sealed record ColumnLayout(int Column, double X, double Width);
