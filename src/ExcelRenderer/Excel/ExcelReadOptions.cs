namespace ExcelRenderer.Excel;

/// <summary>Controls conversion of workbook cells to display text.</summary>
public sealed record ExcelReadOptions
{
    // Keep the serialized legacy value recognizable without using the deprecated API.
    private const ReaderBackend RemovedBackend = (ReaderBackend)0;

    /// <summary>Gets the reader policy. OpenXml is the only implementation.</summary>
    public ReaderBackend Backend { get; init; } = ReaderBackend.OpenXml;

    /// <summary>Gets an optional callback for the preflight engine selection.</summary>
    public Action<ReaderBackendSelection>? BackendSelected { get; init; }

    /// <summary>Gets the formula display policy.</summary>
    public FormulaDisplayMode FormulaDisplayMode { get; init; } = FormulaDisplayMode.CachedValue;

    /// <summary>Gets optional limits for the limited formula calculation session.</summary>
    public Formulas.FormulaEvaluationOptions? FormulaEvaluationOptions { get; init; }

    /// <summary>Gets the conversion's explicit finite source region for default-style materialization.</summary>
    internal Func<string, (int FirstRow, int FirstColumn, int LastRow, int LastColumn)?>? GetSelectedRange { get; init; }

    /// <summary>Gets a value indicating whether the consumer includes cells outside print areas.</summary>
    internal bool IgnoreFormulaPrintAreas { get; init; }

    /// <summary>Gets an exact calculation target predicate obtained from a geometry-only page plan.</summary>
    internal Func<string, int, int, bool>? IsFormulaTarget { get; init; }

    /// <summary>Validates the policy before opening input or output.</summary>
    internal void Validate()
    {
        if (!Enum.IsDefined(typeof(ReaderBackend), Backend))
        {
            throw new ArgumentOutOfRangeException(nameof(Backend));
        }

        if (!Enum.IsDefined(typeof(FormulaDisplayMode), FormulaDisplayMode))
        {
            throw new ArgumentOutOfRangeException(nameof(FormulaDisplayMode));
        }

        if (Backend == RemovedBackend)
        {
            throw new NotSupportedException("The ClosedXml backend has been removed. Use OpenXml or Auto.");
        }

        if (FormulaDisplayMode == FormulaDisplayMode.Legacy)
        {
            throw new NotSupportedException("The historical Legacy mode is unsupported. Use CachedValue for saved results or Recalculate for the limited formula engine.");
        }
    }
}
