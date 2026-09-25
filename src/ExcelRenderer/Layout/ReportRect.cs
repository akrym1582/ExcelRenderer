namespace ExcelRenderer.Layout;

/// <summary>
/// 帳票上の左上座標と寸法で定義される矩形領域を表します。
/// </summary>
public readonly record struct ReportRect(double X, double Y, double Width, double Height);
