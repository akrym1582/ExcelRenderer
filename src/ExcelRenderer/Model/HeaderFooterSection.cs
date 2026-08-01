namespace ExcelRenderer.Model;

public sealed record HeaderFooterSection(
    string Left = "",
    string Center = "",
    string Right = "");
