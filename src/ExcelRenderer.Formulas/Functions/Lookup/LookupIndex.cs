using System;
using System.Collections.Generic;
using ExcelRenderer.Formulas.References;
using ExcelRenderer.Formulas.Values;

namespace ExcelRenderer.Formulas.Functions.Lookup
{
    /// <summary>Retains only the evaluated prefix of one lookup vector; no enumerator survives a call.</summary>
    internal sealed class LookupIndex
    {
        /// <summary>Gets the first position of each typed value in the evaluated prefix.</summary>
        internal Dictionary<FormulaValue, long> FirstPositions { get; } = new Dictionary<FormulaValue, long>(new ValueComparer());

        /// <summary>Gets first distinct text values in source order for early wildcard matches.</summary>
        internal List<(long Position, string Text)> TextPositions { get; } = new List<(long Position, string Text)>();

        /// <summary>Gets or sets the first unexamined relative position.</summary>
        internal long Next { get; set; }

        /// <summary>Gets or sets the first ordinary Excel error, which terminates unmatched searches.</summary>
        internal FormulaValue? Error { get; set; }

        /// <summary>Gets or sets a value indicating whether a capacity limit requires streaming fallback.</summary>
        internal bool Disabled { get; set; }

        /// <summary>Compares vector identity without changing sheet-name equality.</summary>
        internal sealed class RangeComparer : IEqualityComparer<FormulaRange>
        {
            /// <inheritdoc/>
            public bool Equals(FormulaRange x, FormulaRange y) => StringComparer.OrdinalIgnoreCase.Equals(x.SheetId, y.SheetId) && x.FirstRow == y.FirstRow && x.FirstColumn == y.FirstColumn && x.LastRow == y.LastRow && x.LastColumn == y.LastColumn;

            /// <inheritdoc/>
            public int GetHashCode(FormulaRange value) => (((((StringComparer.OrdinalIgnoreCase.GetHashCode(value.SheetId) * 397) ^ value.FirstRow) * 397) ^ value.FirstColumn) * 397) ^ value.LastRow ^ value.LastColumn;
        }

        private sealed class ValueComparer : IEqualityComparer<FormulaValue>
        {
            /// <inheritdoc/>
            public bool Equals(FormulaValue x, FormulaValue y) => x.Kind == y.Kind && (x.Kind == FormulaValueKind.Number ? x.Number == y.Number : x.Kind == FormulaValueKind.Boolean ? x.Boolean == y.Boolean : StringComparer.OrdinalIgnoreCase.Equals(x.Text, y.Text));

            /// <inheritdoc/>
            public int GetHashCode(FormulaValue value) => ((int)value.Kind * 397) ^ (value.Kind == FormulaValueKind.Number ? value.Number.GetHashCode() : value.Kind == FormulaValueKind.Boolean ? value.Boolean.GetHashCode() : StringComparer.OrdinalIgnoreCase.GetHashCode(value.Text));
        }
    }
}
