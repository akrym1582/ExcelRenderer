namespace ExcelRenderer.OpenXml.Calculation;

/// <summary>Controls whether saved values or explicitly recalculated formula values are used.</summary>
public enum FormulaCalculationMode
{
    /// <summary>Use the workbook's saved values without calculation.</summary>
    CachedValue,

    /// <summary>Evaluate supported formulas and reject unsupported reached formulas.</summary>
    Recalculate,
}
