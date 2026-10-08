using ExcelRenderer.Slim.Layout;
using ExcelRenderer.Slim.Model;

namespace ExcelRenderer.Slim.Drawing;

/// <summary>
/// 2 点間に指定した線種の直線を描画するコマンドを表します。
/// </summary>
internal sealed record DrawLineCommand(int PageNumber, double X1, double Y1, double X2, double Y2, BorderSide Style) : DrawCommand(PageNumber);
