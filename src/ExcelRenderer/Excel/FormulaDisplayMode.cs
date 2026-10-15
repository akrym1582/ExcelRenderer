namespace ExcelRenderer.Excel;

/// <summary>Selects the source of formula display values.</summary>
public enum FormulaDisplayMode
{
    /// <summary>Preserves the existing formatter and hyperlink behavior.</summary>
    Legacy = 0,

    /// <summary>Uses only results saved in the source workbook.</summary>
    CachedValue = 1,

    /// <summary>Requests fresh formula evaluation.</summary>
    Recalculate = 2,
}
