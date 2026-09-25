namespace ExcelRenderer.Model;

/// <summary>
/// HeaderFooterSection が表すデータと操作を提供します.
/// </summary>
public sealed record HeaderFooterSection(
    string Left = "",
    string Center = "",
    string Right = "");
