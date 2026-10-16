namespace ExcelRenderer.Core;

/// <summary>Selects the source of formula display values.</summary>
public enum FormulaDisplayMode
{
    /// <summary>Unsupported historical mode. Use CachedValue; formulas are never calculated.</summary>
    Legacy = 0,

    /// <summary>Uses only results saved in the source workbook.</summary>
    CachedValue = 1,

    /// <summary>Unsupported calculation request. Calculate values in the caller.</summary>
    Recalculate = 2,
}
