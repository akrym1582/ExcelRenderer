namespace ExcelRenderer.Core.Excel;

/// <summary>Internal formula display policy, independent of the full product API.</summary>
internal enum FormulaDisplayMode
{
    /// <summary>Unsupported historical mode. Use CachedValue; formulas are never calculated.</summary>
    Legacy,

    /// <summary>Uses original saved results.</summary>
    CachedValue,

    /// <summary>Unsupported calculation request. Calculate values in the caller.</summary>
    Recalculate,
}
