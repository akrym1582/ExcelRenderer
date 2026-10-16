using System;
using System.Collections.Generic;
using System.Globalization;

namespace ExcelRenderer.Formulas
{
    /// <summary>Identifies a normalized inclusive rectangular reference.</summary>
    public readonly struct FormulaRange
    {
        /// <summary>Initializes a new instance of the <see cref="FormulaRange"/> struct.</summary>
        /// <param name="sheetId">The sheet id.</param>
        /// <param name="firstRow">The first row.</param>
        /// <param name="firstColumn">The first column.</param>
        /// <param name="lastRow">The last row.</param>
        /// <param name="lastColumn">The last column.</param>
        public FormulaRange(string sheetId, int firstRow, int firstColumn, int lastRow, int lastColumn)
        {
            var first = new FormulaCellAddress(sheetId, firstRow, firstColumn);
            var last = new FormulaCellAddress(sheetId, lastRow, lastColumn);
            SheetId = first.SheetId;
            FirstRow = Math.Min(first.Row, last.Row);
            FirstColumn = Math.Min(first.Column, last.Column);
            LastRow = Math.Max(first.Row, last.Row);
            LastColumn = Math.Max(first.Column, last.Column);
        }

        /// <summary>Gets sheet id.</summary>
        public string SheetId { get; }

        /// <summary>Gets first row.</summary>
        public int FirstRow { get; }

        /// <summary>Gets first column.</summary>
        public int FirstColumn { get; }

        /// <summary>Gets last row.</summary>
        public int LastRow { get; }

        /// <summary>Gets last column.</summary>
        public int LastColumn { get; }
    }
}
