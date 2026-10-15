namespace ExcelRenderer.Excel;

/// <summary>Controls conversion of workbook cells to display text.</summary>
public sealed record ExcelReadOptions
{
    /// <summary>Gets the formula display policy.</summary>
    public FormulaDisplayMode FormulaDisplayMode { get; init; } = FormulaDisplayMode.Legacy;

    /// <summary>Validates the policy before opening input or output.</summary>
    internal void Validate()
    {
        if (!Enum.IsDefined(typeof(FormulaDisplayMode), FormulaDisplayMode))
        {
            throw new ArgumentOutOfRangeException(nameof(FormulaDisplayMode));
        }

        if (FormulaDisplayMode == FormulaDisplayMode.Recalculate)
        {
            throw new NotSupportedException("Selected-root formula recalculation is not supported by the pinned ClosedXML public API.");
        }
    }
}
