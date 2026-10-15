namespace ExcelRenderer.Core.Excel;

/// <summary>Internal formula display policy, independent of the full product API.</summary>
internal enum FormulaDisplayMode
{
    /// <summary>Preserves legacy display semantics.</summary>
    Legacy,

    /// <summary>Uses original saved results.</summary>
    CachedValue,

    /// <summary>Requests fresh evaluation.</summary>
    Recalculate,
}
