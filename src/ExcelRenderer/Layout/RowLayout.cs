using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

public sealed record RowLayout(int Row, double Y, double Height);
