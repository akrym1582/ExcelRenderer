namespace ExcelRenderer.Slim.Layout;

/// <summary>
/// 帳票上の左上座標と寸法で定義される矩形領域を表します。
/// </summary>
internal readonly record struct ReportRect(double X, double Y, double Width, double Height);
