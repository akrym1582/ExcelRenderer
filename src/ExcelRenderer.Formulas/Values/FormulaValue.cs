using System;
using System.Collections.Generic;
using System.Globalization;

namespace ExcelRenderer.Formulas
{
    /// <summary>Stores a blank, finite number, text, Boolean or Excel error.</summary>
    public readonly struct FormulaValue
    {
        private readonly string? text;

        private FormulaValue(FormulaValueKind kind, double number = 0, string? text = null, bool boolean = false)
        {
            Kind = kind;
            Number = number;
            this.text = text;
            Boolean = boolean;
        }

        /// <summary>Gets the value of an unset cell.</summary>
        public static FormulaValue Blank => default;

        /// <summary>Gets the stored value kind.</summary>
        public FormulaValueKind Kind { get; }

        /// <summary>Gets the numeric payload.</summary>
        public double Number { get; }

        /// <summary>Gets the text or error payload, or an empty string when absent.</summary>
        public string Text => text ?? string.Empty;

        /// <summary>Gets a value indicating whether the logical value is true.</summary>
        public bool Boolean { get; }

        /// <summary>Gets the Excel error literal.</summary>
        public string Error => Text;

        /// <summary>Creates a finite numeric value, rejecting NaN and infinity.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The resulting value.</returns>
        public static FormulaValue FromNumber(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            return new FormulaValue(FormulaValueKind.Number, value);
        }

        /// <summary>Creates a text value, including an empty string.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The resulting value.</returns>
        public static FormulaValue FromText(string value) => new FormulaValue(FormulaValueKind.Text, text: value ?? throw new ArgumentNullException(nameof(value)));

        /// <summary>Creates a logical value.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The resulting value.</returns>
        public static FormulaValue FromBoolean(bool value) => new FormulaValue(FormulaValueKind.Boolean, boolean: value);

        /// <summary>Creates an Excel error value.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The resulting value.</returns>
        public static FormulaValue FromError(string value) => new FormulaValue(FormulaValueKind.Error, text: value ?? throw new ArgumentNullException(nameof(value)));

        /// <summary>Formats the value using the invariant v1 concatenation profile.</summary>
        /// <returns>The resulting value.</returns>
        public override string ToString() => Kind == FormulaValueKind.Number ? Number.ToString("G15", CultureInfo.InvariantCulture) : Kind == FormulaValueKind.Boolean ? (Boolean ? "TRUE" : "FALSE") : Text ?? string.Empty;
    }
}
