namespace ExcelRenderer.Model;

/// <summary>
/// ヘッダーまたはフッターの左、中央、および右の各領域に表示する文字列を表します。
/// </summary>
public sealed record HeaderFooterSection(
    string Left = "",
    string Center = "",
    string Right = "");
