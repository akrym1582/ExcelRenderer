namespace ExcelRenderer.Model;

/// <summary>
/// 罫線の描画に使用する線種を表します。
/// </summary>
public enum BorderLineStyle
{
    /// <summary>
    /// 切れ目のない実線を表します。
    /// </summary>
    Solid,

    /// <summary>
    /// 点を連ねた点線を表します。
    /// </summary>
    Dotted,

    /// <summary>
    /// 短い線を連ねた破線を表します。
    /// </summary>
    Dashed,

    /// <summary>
    /// 破線と点を交互に並べた一点鎖線を表します。
    /// </summary>
    DashDot,

    /// <summary>
    /// 破線と 2 点を交互に並べた二点鎖線を表します。
    /// </summary>
    DashDotDot,

    /// <summary>平行な 2 本の実線を表します。</summary>
    Double,

    /// <summary>極細の実線を表します。</summary>
    Hair,

    /// <summary>斜めの破線と点を交互に並べた線を表します。</summary>
    SlantDashDot,
}
