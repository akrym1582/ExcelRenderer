namespace ExcelRenderer.Slim.Rendering;

/// <summary>変換診断を重複排除し、設定に従って保持する内部コレクターです。</summary>
internal sealed class DiagnosticCollector
{
    private const int MaximumDiagnostics = 100;
    private readonly Dictionary<string, ConversionDiagnostic> _diagnostics = new(StringComparer.Ordinal);
    private int _discarded;

    /// <summary>診断を追加し、同じ発生箇所の診断は発生回数へ集約します。</summary>
    /// <param name="diagnostic">追加する診断です。</param>
    public void Add(ConversionDiagnostic diagnostic)
    {
        var key = string.Join("\u001f", diagnostic.Code, diagnostic.SheetName, diagnostic.CellRange, diagnostic.ObjectId, diagnostic.SourcePageNumber);
        if (_diagnostics.TryGetValue(key, out var existing))
        {
            _diagnostics[key] = existing with { OccurrenceCount = existing.OccurrenceCount + diagnostic.OccurrenceCount };
            return;
        }

        if (_diagnostics.Count >= MaximumDiagnostics)
        {
            _discarded++;
            return;
        }

        _diagnostics.Add(key, diagnostic);
    }

    /// <summary>保持した診断を一覧として取得します。</summary>
    /// <returns>重複を集約した診断の一覧を返します。上限超過時は末尾に切り捨て通知を含めます。</returns>
    public IReadOnlyList<ConversionDiagnostic> ToArray()
    {
        var result = _diagnostics.Values.ToList();
        if (_discarded > 0)
        {
            result.Add(new(
                "DiagnosticsTruncated",
                DiagnosticSeverity.Warning,
                DiagnosticStage.Read,
                $"Diagnostic collection reached its limit; {_discarded} additional diagnostics were discarded.",
                OccurrenceCount: _discarded));
        }

        return result;
    }
}
