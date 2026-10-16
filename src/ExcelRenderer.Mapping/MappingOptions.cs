using System;
using System.Globalization;
using ExcelRenderer.Formulas;
using ExcelRenderer.OpenXml;
using ExcelRenderer.OpenXml.Input;

namespace ExcelRenderer.Mapping;

/// <summary>Controls formatting and the maximum expanded worksheet size.</summary>
public sealed class MappingOptions
{
    /// <summary>Gets or sets formula handling. Recalculate enables reference rewriting and refreshes all saved formula values.</summary>
    public FormulaCalculationMode FormulaCalculationMode { get; set; } = FormulaCalculationMode.CachedValue;

    /// <summary>Gets or sets optional resource limits for mapped workbook recalculation.</summary>
    public FormulaEvaluationOptions? FormulaEvaluationOptions { get; set; }

    /// <summary>Gets or sets a value indicating whether Recalculate mode evaluates all mapped formulas before saving. Set false when a subsequent renderer evaluates selected cells; all saved caches are invalidated.</summary>
    public bool RecalculateMappedWorkbook { get; set; } = true;

    /// <summary>Gets or sets the editing engine. The default is OpenXml.</summary>
    public MappingBackend Backend { get; set; } = MappingBackend.OpenXml;

    /// <summary>Gets or sets an optional preflight engine-selection callback.</summary>
    public Action<MappingBackendSelection>? BackendSelected { get; set; }

    /// <summary>Gets or sets input limits and shared stage buffering settings.</summary>
    public WorkbookInputOptions Input { get; set; } = new WorkbookInputOptions();

    /// <summary>Gets or sets the default formatting culture.</summary>
    public CultureInfo Culture { get; set; } = CultureInfo.InvariantCulture;

    /// <summary>Gets or sets the maximum number of output rows per worksheet.</summary>
    public int MaxOutputRows { get; set; } = 100_000;
}
