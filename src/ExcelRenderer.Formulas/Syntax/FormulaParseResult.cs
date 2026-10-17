using System;
using System.Collections.Generic;

namespace ExcelRenderer.Formulas;

/// <summary>A syntax tree or explicit parser diagnostics.</summary>
public sealed class FormulaParseResult
{
    /// <summary>Initializes a new instance of the <see cref="FormulaParseResult"/> class.</summary>
    /// <param name="formula">The formula value.</param>
    /// <param name="diagnostics">The diagnostics value.</param>
    internal FormulaParseResult(ParsedFormula? formula, params FormulaDiagnostic[] diagnostics)
    {
        Formula = formula;
        Diagnostics = Array.AsReadOnly(diagnostics);
    }

    /// <summary>Gets the parsed formula on success.</summary>
    public ParsedFormula? Formula { get; }

    /// <summary>Gets parse diagnostics.</summary>
    public IReadOnlyList<FormulaDiagnostic> Diagnostics { get; }

    /// <summary>Gets a value indicating whether parsing succeeded.</summary>
    public bool Success => Formula != null;
}
