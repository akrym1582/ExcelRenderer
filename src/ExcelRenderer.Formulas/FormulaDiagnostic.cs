using System;
using System.Collections.Generic;
using System.Globalization;

namespace ExcelRenderer.Formulas
{
    /// <summary>Describes an engine failure separately from an Excel error value.</summary>
    public sealed class FormulaDiagnostic
    {
        /// <summary>Initializes a new instance of the <see cref="FormulaDiagnostic"/> class.</summary>
        /// <param name="code">The code.</param>
        /// <param name="message">The message.</param>
        /// <param name="span">The span.</param>
        /// <param name="cell">The cell.</param>
        /// <param name="feature">The feature.</param>
        /// <param name="dependencyPath">The dependency path.</param>
        public FormulaDiagnostic(string code, string message, SourceSpan span = default, FormulaCellAddress? cell = null, string? feature = null, IReadOnlyList<FormulaCellAddress>? dependencyPath = null)
        {
            Code = code;
            Message = message;
            Span = span;
            Cell = cell;
            Feature = feature;
            DependencyPath = dependencyPath == null ? Array.Empty<FormulaCellAddress>() : new List<FormulaCellAddress>(dependencyPath).AsReadOnly();
        }

        /// <summary>Gets code.</summary>
        public string Code { get; }

        /// <summary>Gets message.</summary>
        public string Message { get; }

        /// <summary>Gets span.</summary>
        public SourceSpan Span { get; }

        /// <summary>Gets cell.</summary>
        public FormulaCellAddress? Cell { get; }

        /// <summary>Gets feature.</summary>
        public string? Feature { get; }

        /// <summary>Gets dependency path.</summary>
        public IReadOnlyList<FormulaCellAddress> DependencyPath { get; }

        /// <summary>Formats the value using the invariant v1 concatenation profile.</summary>
        /// <returns>The resulting value.</returns>
        public override string ToString() => Code + ": " + Cell + " " + Message;
    }
}
