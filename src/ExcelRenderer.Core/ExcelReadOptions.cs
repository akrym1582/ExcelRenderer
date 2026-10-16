namespace ExcelRenderer.Core;

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

    /// <summary>Gets the conversion's explicit finite source region for default-style materialization.</summary>
    internal Func<string, (int FirstRow, int FirstColumn, int LastRow, int LastColumn)?>? GetSelectedRange { get; init; }

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

        if (FormulaDisplayMode != FormulaDisplayMode.CachedValue)
        {
            throw new NotSupportedException("ExcelRenderer does not calculate formulas. Use CachedValue to display saved results; calculate Mapping values in the caller.");
        }
    }
}
