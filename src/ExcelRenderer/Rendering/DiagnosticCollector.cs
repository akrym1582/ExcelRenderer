namespace ExcelRenderer.Rendering;

/// <summary>変換診断を重複排除し、設定に従って保持する内部コレクターです。</summary>
internal sealed class DiagnosticCollector
{
    private readonly DiagnosticOptions _options;
    private readonly Core.Rendering.DiagnosticStore<ConversionDiagnostic> store;
    private bool _failure;

    /// <summary>Initializes a new instance of the <see cref="DiagnosticCollector"/> class. 指定した診断方針を使用するコレクターを初期化します。</summary>
    /// <param name="options">診断の上限、抑制、および失敗判定を指定する設定です。</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> が <see langword="null"/> の場合にスローされます。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="options"/> の診断上限が正でない場合にスローされます。</exception>
    public DiagnosticCollector(DiagnosticOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (options.MaxDiagnostics <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxDiagnostics must be positive.");
        }

        store = new(options.MaxDiagnostics, (existing, diagnostic) => existing with { OccurrenceCount = existing.OccurrenceCount + diagnostic.OccurrenceCount });
    }

    /// <summary>Gets a value indicating whether a diagnostic should fail conversion. 診断方針により変換を失敗として扱うべき診断が存在するかどうかを取得します。</summary>
    public bool HasFailure => _failure;

    /// <summary>診断を追加し、同じ発生箇所の診断は発生回数へ集約します。</summary>
    /// <param name="diagnostic">追加する診断です。</param>
    public void Add(ConversionDiagnostic diagnostic)
    {
        _failure |= diagnostic.Severity == DiagnosticSeverity.Error ||
            (_options.StrictMode && diagnostic.Severity != DiagnosticSeverity.Info) ||
            _options.TreatAsErrors.Contains(diagnostic.Code, StringComparer.Ordinal);
        var key = string.Join("\u001f", diagnostic.Code, diagnostic.SheetName, diagnostic.CellRange, diagnostic.ObjectId, diagnostic.SourcePageNumber, diagnostic.UnicodeSequence);
        store.Add(key, diagnostic, _options.SuppressedCodes.Contains(diagnostic.Code, StringComparer.Ordinal));
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
