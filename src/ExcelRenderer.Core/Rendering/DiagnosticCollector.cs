namespace ExcelRenderer.Core.Rendering;

/// <summary>変換診断を重複排除し、設定に従って保持する内部コレクターです。</summary>
internal sealed class DiagnosticCollector
{
    private const int MaximumDiagnostics = 100;
    private readonly DiagnosticStore<ConversionDiagnostic> store = new(MaximumDiagnostics, (existing, diagnostic) => existing with { OccurrenceCount = existing.OccurrenceCount + diagnostic.OccurrenceCount });
    private readonly Action<ConversionDiagnostic>? forward;

    /// <summary>Initializes a new instance of the <see cref="DiagnosticCollector"/> class.</summary>
    /// <param name="forward">An optional product collector that owns limits and aggregation.</param>
    internal DiagnosticCollector(Action<ConversionDiagnostic>? forward = null) => this.forward = forward;

    /// <summary>診断を追加し、同じ発生箇所の診断は発生回数へ集約します。</summary>
    /// <param name="diagnostic">追加する診断です。</param>
    public void Add(ConversionDiagnostic diagnostic)
    {
        if (forward is not null)
        {
            forward(diagnostic);
            return;
        }

        var key = string.Join("\u001f", diagnostic.Code, diagnostic.SheetName, diagnostic.CellRange, diagnostic.ObjectId, diagnostic.SourcePageNumber);
        store.Add(key, diagnostic);
    }

    /// <summary>保持した診断を一覧として取得します。</summary>
    /// <returns>重複を集約した診断の一覧を返します。上限超過時は末尾に切り捨て通知を含めます。</returns>
    public IReadOnlyList<ConversionDiagnostic> ToArray()
    {
        var result = store.Values.ToList();
        if (store.Discarded > 0)
        {
            result.Add(new(
                "DiagnosticsTruncated",
                DiagnosticSeverity.Warning,
                DiagnosticStage.Read,
                $"Diagnostic collection reached its limit; {store.Discarded} additional diagnostics were discarded.",
                OccurrenceCount: store.Discarded));
        }

        return result;
    }
}
