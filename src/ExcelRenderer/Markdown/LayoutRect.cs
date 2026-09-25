using ExcelRenderer.Model;

namespace ExcelRenderer.Markdown;

/// <summary>
/// ワークシート上の位置と大きさをポイント単位で表す矩形です。
/// </summary>
public readonly record struct LayoutRect(double X, double Y, double Width, double Height)
{
    /// <summary>
    /// Gets the right edge coordinate. 矩形の右端の X 座標を取得します。
    /// </summary>
    public double Right => X + Width;

    /// <summary>
    /// Gets the bottom edge coordinate. 矩形の下端の Y 座標を取得します。
    /// </summary>
    public double Bottom => Y + Height;

    /// <summary>
    /// 指定した座標が矩形の境界上または内部にあるかを判定します。
    /// </summary>
    /// <param name="x">判定する点の X 座標。</param>
    /// <param name="y">判定する点の Y 座標。</param>
    /// <returns>座標が矩形の境界上または内部にある場合は <see langword="true"/>、それ以外は <see langword="false"/>。</returns>
    public bool Contains(double x, double y) => x >= X && x <= Right && y >= Y && y <= Bottom;
}
