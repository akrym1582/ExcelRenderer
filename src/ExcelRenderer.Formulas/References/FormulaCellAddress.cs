using System;
using System.Collections.Generic;
using System.Globalization;

namespace ExcelRenderer.Formulas.References
{
    /// <summary>Identifies a one-based cell coordinate in a workbook sheet.</summary>
    public readonly struct FormulaCellAddress : IEquatable<FormulaCellAddress>
    {
        /// <summary>Initializes a new instance of the <see cref="FormulaCellAddress"/> struct.</summary>
        /// <param name="sheetId">The sheet id.</param>
        /// <param name="row">The row.</param>
        /// <param name="column">The column.</param>
        public FormulaCellAddress(string sheetId, int row, int column)
        {
            SheetId = sheetId ?? throw new ArgumentNullException(nameof(sheetId));
            if (row < 1 || row > 1048576 || column < 1 || column > 16384)
            {
                throw new ArgumentOutOfRangeException(nameof(row));
            }

            Row = row;
            Column = column;
        }

        /// <summary>Gets sheet id.</summary>
        public string SheetId { get; }

        /// <summary>Gets row.</summary>
        public int Row { get; }

        /// <summary>Gets column.</summary>
        public int Column { get; }

        /// <summary>Compares sheet identifiers without case and compares coordinates exactly.</summary>
        /// <param name="other">The other.</param>
        /// <returns>The resulting value.</returns>
        public bool Equals(FormulaCellAddress other) => StringComparer.OrdinalIgnoreCase.Equals(SheetId, other.SheetId) && Row == other.Row && Column == other.Column;

        /// <summary>Compares sheet identifiers without case and compares coordinates exactly.</summary>
        /// <param name="obj">The obj.</param>
        /// <returns>The resulting value.</returns>
        public override bool Equals(object? obj) => obj is FormulaCellAddress other && Equals(other);

        /// <summary>Returns a hash consistent with coordinate equality.</summary>
        /// <returns>The resulting value.</returns>
        public override int GetHashCode() => ((StringComparer.OrdinalIgnoreCase.GetHashCode(SheetId) * 397) ^ Row) * 397 ^ Column;

        /// <summary>Formats the value using the invariant v1 concatenation profile.</summary>
        /// <returns>The resulting value.</returns>
        public override string ToString() => SheetId + "!R" + Row + "C" + Column;
    }
}
