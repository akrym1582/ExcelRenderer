namespace ExcelRenderer.Model;

public sealed record ReportDocument(IReadOnlyList<ReportSheet> Sheets);
