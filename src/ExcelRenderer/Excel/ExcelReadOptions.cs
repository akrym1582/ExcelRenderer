namespace ExcelRenderer.Excel;

/// <summary>Controls workbook display, conditional formatting, and source layout metrics.</summary>
public sealed record ExcelReadOptions
{
    /// <summary>Gets a value indicating whether supported saved conditional formatting is evaluated and applied. Defaults to true; Core does not offer this option.</summary>
    public bool ApplyConditionalFormatting { get; init; } = true;

    /// <summary>Gets the Normal-font metrics used for column widths, independently of drawing fonts.</summary>
    public ColumnWidthMetricsOptions ColumnWidthMetrics { get; init; } = new();

    /// <summary>Gets the formula display policy.</summary>
    public FormulaDisplayMode FormulaDisplayMode { get; init; } = FormulaDisplayMode.CachedValue;

    /// <summary>Gets optional limits for the limited formula calculation session.</summary>
    public Formulas.Evaluation.FormulaEvaluationOptions? FormulaEvaluationOptions { get; init; }

    /// <summary>Gets the conversion-local metric decisions shared by geometry preflight and rendering.</summary>
    internal CoreIntegration.FullFontMetrics.MetricCache? ColumnWidthMetricsCache { get; init; }

    /// <summary>Gets the conversion's explicit finite source region for default-style materialization.</summary>
    internal Func<string, (int FirstRow, int FirstColumn, int LastRow, int LastColumn)?>? GetSelectedRange { get; init; }

    /// <summary>Gets a value indicating whether the consumer includes cells outside print areas.</summary>
    internal bool IgnoreFormulaPrintAreas { get; init; }

    /// <summary>Gets an exact calculation target predicate obtained from a geometry-only page plan.</summary>
    internal Func<string, int, int, bool>? IsFormulaTarget { get; init; }

    /// <summary>Gets the geometry-only page predicate, including missing cells inside visible page regions.</summary>
    internal Func<string, int, int, bool>? IsConditionalFormattingTarget { get; init; }

    /// <summary>Gets all target-policy transition rows and columns from selected page geometry.</summary>
    internal Func<string, (IReadOnlyList<int> Rows, IReadOnlyList<int> Columns)?>? ConditionalTargetBoundaries { get; init; }

    /// <summary>Validates the policy before opening input or output.</summary>
    internal void Validate()
    {
        (ColumnWidthMetrics ?? throw new ArgumentNullException(nameof(ColumnWidthMetrics))).Validate();
        if (!Enum.IsDefined(typeof(FormulaDisplayMode), FormulaDisplayMode))
        {
            throw new ArgumentOutOfRangeException(nameof(FormulaDisplayMode));
        }

        if (FormulaDisplayMode == FormulaDisplayMode.Legacy)
        {
            throw new NotSupportedException("The historical Legacy mode is unsupported. Use CachedValue for saved results or Recalculate for the limited formula engine.");
        }
    }
}
