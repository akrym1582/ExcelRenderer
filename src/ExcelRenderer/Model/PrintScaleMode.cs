namespace ExcelRenderer.Model;

/// <summary>
/// ワークシートの印刷倍率を決定する方法を指定します。
/// </summary>
public enum PrintScaleMode
{
    /// <summary>
    /// <see cref="PageSettings.Scale"/> の明示倍率を使用します。
    /// </summary>
    Explicit,

    /// <summary>
    /// 指定した横および縦のページ数に収まる倍率を使用します。
    /// </summary>
    FitToPages,
}
