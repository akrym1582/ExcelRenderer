using ExcelRenderer.Layout;
using ExcelRenderer.Model;

namespace ExcelRenderer.Drawing;

/// <summary>
/// 指定した矩形内へ塗り、輪郭線、および文字を持つ図形を描画するコマンドを表します。
/// </summary>
public sealed record DrawShapeCommand(int PageNumber, ReportRect Bounds, ReportShape Shape) : DrawCommand(PageNumber);
