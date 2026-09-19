using ExcelRenderer.Layout;
using ExcelRenderer.Model;
namespace ExcelRenderer.Drawing;
public sealed record DrawShapeCommand(int PageNumber, ReportRect Bounds, ReportShape Shape) : DrawCommand(PageNumber);
