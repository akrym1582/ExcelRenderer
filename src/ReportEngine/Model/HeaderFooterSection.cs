namespace ReportEngine.Model;

public sealed record HeaderFooterSection(
    string Left = "",
    string Center = "",
    string Right = "");
