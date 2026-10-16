using System;
using System.Collections.Generic;
using System.Globalization;

namespace ExcelRenderer.Formulas
{
    /// <summary>Contains either a constant value or formula text without a saved cache.</summary>
    public readonly struct FormulaCellData
    {
        private FormulaCellData(FormulaValue value, string? formula)
        {
            Value = value;
            Formula = formula;
        }

        /// <summary>Gets the value of an unset cell.</summary>
        public static FormulaCellData Blank => default;

        /// <summary>Gets value.</summary>
        public FormulaValue Value { get; }

        /// <summary>Gets formula.</summary>
        public string? Formula { get; }

        /// <summary>Provides from value.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The resulting value.</returns>
        public static FormulaCellData FromValue(FormulaValue value) => new FormulaCellData(value, null);

        /// <summary>Provides from formula.</summary>
        /// <param name="formula">The formula.</param>
        /// <returns>The resulting value.</returns>
        public static FormulaCellData FromFormula(string formula) => new FormulaCellData(default, formula ?? throw new ArgumentNullException(nameof(formula)));
    }
}
