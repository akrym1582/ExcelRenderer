using ExcelRenderer.Model;

namespace ExcelRenderer.Drawing;

/// <summary>罫線の線種に応じて描画する線分の座標を求めます。</summary>
internal static class BorderStrokeGeometry
{
    /// <summary>線種に応じた 1 本または複数の描画線分を返します。</summary>
    /// <param name="side">描画する罫線の書式です。</param>
    /// <param name="x1">始点の X 座標です。</param>
    /// <param name="y1">始点の Y 座標です。</param>
    /// <param name="x2">終点の X 座標です。</param>
    /// <param name="y2">終点の Y 座標です。</param>
    /// <param name="inwardX">セル内部を指す X 方向です。</param>
    /// <param name="inwardY">セル内部を指す Y 方向です。</param>
    /// <returns>実際に描画する線分の座標です。</returns>
    internal static IReadOnlyList<(double X1, double Y1, double X2, double Y2)> GetStrokes(
        BorderSide side,
        double x1,
        double y1,
        double x2,
        double y2,
        double inwardX = 0,
        double inwardY = 0)
    => Core.Drawing.BorderStrokeGeometry.GetStrokes(
        side.LineStyle switch
        {
            BorderLineStyle.Double => Core.Model.BorderLineStyle.Double,
            BorderLineStyle.SlantDashDot => Core.Model.BorderLineStyle.SlantDashDot,
            _ => Core.Model.BorderLineStyle.Solid,
        },
        side.Width,
        x1,
        y1,
        x2,
        y2,
        inwardX,
        inwardY);
}
