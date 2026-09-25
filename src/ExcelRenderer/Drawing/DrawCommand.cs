using ExcelRenderer.Layout;
using ExcelRenderer.Model;

namespace ExcelRenderer.Drawing;

/// <summary>
/// DrawCommand が表すデータと操作を提供します.
/// </summary>
public abstract record DrawCommand(int PageNumber);
