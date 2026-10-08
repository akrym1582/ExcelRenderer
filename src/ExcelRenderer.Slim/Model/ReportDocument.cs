namespace ExcelRenderer.Slim.Model;

/// <summary>
/// 読み込まれた帳票を構成するワークシートの集合を表します。
/// </summary>
internal sealed record ReportDocument(IReadOnlyList<ReportSheet> Sheets);
