using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ExcelRenderer.Formulas;

namespace ExcelRenderer.OpenXml;

/// <summary>Reports a formula that could not safely be recalculated.</summary>
public sealed class FormulaCalculationException : InvalidOperationException
{
    /// <summary>Initializes a new instance of the <see cref="FormulaCalculationException"/> class.</summary>
    /// <param name="message">Cell location and failure description.</param>
    public FormulaCalculationException(string message)
        : base(message)
    {
        Diagnostics = Array.Empty<FormulaDiagnostic>();
    }

    /// <summary>Initializes a new instance of the <see cref="FormulaCalculationException"/> class with complete engine diagnostics.</summary>
    /// <param name="target">Requested calculation target.</param>
    /// <param name="diagnostics">Failures retaining source spans, features and dependency paths.</param>
    public FormulaCalculationException(FormulaCellAddress target, IReadOnlyList<FormulaDiagnostic> diagnostics)
        : base(Format(target, diagnostics ?? throw new ArgumentNullException(nameof(diagnostics))))
    {
        Diagnostics = new List<FormulaDiagnostic>(diagnostics).AsReadOnly();
    }

    /// <summary>Gets immutable engine diagnostics, or an empty list for a source-only failure.</summary>
    public IReadOnlyList<FormulaDiagnostic> Diagnostics { get; }

    private static string Format(FormulaCellAddress target, IReadOnlyList<FormulaDiagnostic> diagnostics)
    {
        return Address(target) + ": " + string.Join("; ", diagnostics.Select(diagnostic =>
            diagnostic.Code + ": " + diagnostic.Message +
            (diagnostic.Cell is { } cell ? " [cell " + Address(cell) + "]" : string.Empty) +
            " [span " + diagnostic.Span.Start.ToString(CultureInfo.InvariantCulture) + ":" + diagnostic.Span.Length.ToString(CultureInfo.InvariantCulture) + "]" +
            (diagnostic.Feature == null ? string.Empty : " [feature " + diagnostic.Feature + "]") +
            (diagnostic.DependencyPath.Count == 0 ? string.Empty : " [path " + string.Join(" -> ", diagnostic.DependencyPath.Select(Address)) + "]")));
    }

    private static string Address(FormulaCellAddress address) => address.SheetId + "!" + new CellAddress(address.Row, address.Column);
}
