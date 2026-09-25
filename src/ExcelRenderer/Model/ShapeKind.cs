namespace ExcelRenderer.Model;

/// <summary>
/// 帳票上に描画できる図形の幾何形状を表します。
/// </summary>
public enum ShapeKind
{
    /// <summary>
    /// 角が直角の長方形を表します。
    /// </summary>
    Rectangle,

    /// <summary>
    /// 角を丸めた長方形を表します。
    /// </summary>
    RoundedRectangle,

    /// <summary>
    /// 楕円形を表します。
    /// </summary>
    Ellipse,

    /// <summary>
    /// 長方形にくさび形の引き出し線を付けた吹き出しを表します。
    /// </summary>
    WedgeRectangleCallout,

    /// <summary>
    /// 角丸長方形にくさび形の引き出し線を付けた吹き出しを表します。
    /// </summary>
    WedgeRoundedRectangleCallout,
}
