namespace ReportEngine.Model;

public sealed record ReportDocument(IReadOnlyList<ReportSheet> Sheets);
