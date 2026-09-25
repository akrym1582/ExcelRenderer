using ExcelRenderer.Model;

namespace ExcelRenderer.Markdown;

/// <summary>
/// LayoutRect が表すデータと操作を提供します.
/// </summary>
public readonly record struct LayoutRect(double X, double Y, double Width, double Height)
{
    /// <summary>
    /// Gets the value. 対応する値を取得または設定します.
    /// </summary>
    public double Right => X + Width;

    /// <summary>
    /// Gets the value. 対応する値を取得または設定します.
    /// </summary>
    public double Bottom => Y + Height;

    /// <summary>
    /// Contains を実行します.
    /// </summary>
    /// <param name="x">x に渡す値です。</param>
    /// <param name="y">y に渡す値です。</param>
    /// <returns>処理によって得られた結果を返します。</returns>
    public bool Contains(double x, double y) => x >= X && x <= Right && y >= Y && y <= Bottom;
}
