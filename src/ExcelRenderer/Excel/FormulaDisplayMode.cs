namespace ExcelRenderer.Excel;

/// <summary>Selects the source of formula display values.</summary>
public enum FormulaDisplayMode
{
    /// <summary>Unsupported historical mode. Use CachedValue or Recalculate.</summary>
    Legacy = 0,

    /// <summary>Uses only results saved in the source workbook.</summary>
    CachedValue = 1,

    /// <summary>Evaluates selected formula cells and their dependencies using the limited built-in engine.</summary>
    Recalculate = 2,
}
