namespace ExcelRenderer.Model;

/// <summary>
/// 読み込まれた帳票を構成するワークシートの集合を表します。
/// </summary>
public sealed record ReportDocument(IReadOnlyList<ReportSheet> Sheets);
