using System;
using System.Collections.Generic;
using System.Globalization;

namespace ExcelRenderer.Formulas
{
    /// <summary>Initializes a new instance of the <see cref="FormulaValueKind"/> class.</summary>
    public enum FormulaValueKind
    {
        /// <summary>Blank value.</summary>
        Blank,

        /// <summary>Number value.</summary>
        Number,

        /// <summary>Text value.</summary>
        Text,

        /// <summary>Boolean value.</summary>
        Boolean,

        /// <summary>Error value.</summary>
        Error,
    }
}
