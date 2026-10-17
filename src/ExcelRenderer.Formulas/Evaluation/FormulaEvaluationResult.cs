using System;
using System.Collections.Generic;
using System.Globalization;

namespace ExcelRenderer.Formulas
{
    /// <summary>Contains a typed result or an engine failure diagnostic.</summary>
    public sealed class FormulaEvaluationResult
    {
        /// <summary>Initializes a new instance of the <see cref="FormulaEvaluationResult"/> class.</summary>
        /// <param name="value">The value.</param>
        internal FormulaEvaluationResult(FormulaValue value)
        {
            Value = value;
            Diagnostics = Array.Empty<FormulaDiagnostic>();
        }

        /// <summary>Initializes a new instance of the <see cref="FormulaEvaluationResult"/> class.</summary>
        /// <param name="diagnostic">The diagnostic.</param>
        internal FormulaEvaluationResult(FormulaDiagnostic diagnostic)
        {
            Diagnostics = Array.AsReadOnly(new[] { diagnostic });
        }

        /// <summary>Gets a value indicating whether evaluation succeeded.</summary>
        public bool Success => Diagnostics.Count == 0;

        /// <summary>Gets value.</summary>
        public FormulaValue Value { get; }

        /// <summary>Gets diagnostics.</summary>
        public IReadOnlyList<FormulaDiagnostic> Diagnostics { get; }
    }
}
