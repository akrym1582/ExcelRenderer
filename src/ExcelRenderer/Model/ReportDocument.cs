namespace ExcelRenderer.Model;

/// <summary>
/// ReportDocument が表すデータと操作を提供します.
/// </summary>
public sealed record ReportDocument(IReadOnlyList<ReportSheet> Sheets);
