namespace ExcelRenderer.Rendering;

internal sealed class DiagnosticCollector
{
    private readonly DiagnosticOptions _options;
    private readonly Dictionary<string, ConversionDiagnostic> _diagnostics = new(StringComparer.Ordinal);
    private int _discarded;
    private bool _failure;

    public DiagnosticCollector(DiagnosticOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (options.MaxDiagnostics <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxDiagnostics must be positive.");
        }
    }

    public void Add(ConversionDiagnostic diagnostic)
    {
        _failure |= diagnostic.Severity == DiagnosticSeverity.Error ||
            _options.StrictMode && diagnostic.Severity != DiagnosticSeverity.Info ||
            _options.TreatAsErrors.Contains(diagnostic.Code, StringComparer.Ordinal);
        var key = string.Join("\u001f", diagnostic.Code, diagnostic.SheetName, diagnostic.CellRange, diagnostic.ObjectId, diagnostic.SourcePageNumber);
        if (_diagnostics.TryGetValue(key, out var existing))
        {
            _diagnostics[key] = existing with { OccurrenceCount = existing.OccurrenceCount + diagnostic.OccurrenceCount };
            return;
        }

        if (_diagnostics.Count >= _options.MaxDiagnostics)
        {
            _discarded++;
            return;
        }

        if (!_options.SuppressedCodes.Contains(diagnostic.Code, StringComparer.Ordinal))
        {
            _diagnostics.Add(key, diagnostic);
        }
    }

    public bool HasFailure => _failure;

    public IReadOnlyList<ConversionDiagnostic> ToArray()
    {
        var result = _diagnostics.Values.ToList();
        if (_discarded > 0)
        {
            result.Add(new("DiagnosticsTruncated", DiagnosticSeverity.Warning, DiagnosticStage.Read,
                $"Diagnostic collection reached its limit; {_discarded} additional diagnostics were discarded.", OccurrenceCount: _discarded));
        }

        return result;
    }
}
