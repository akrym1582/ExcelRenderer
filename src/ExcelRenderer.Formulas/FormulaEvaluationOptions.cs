using System;
using System.Collections.Generic;
using System.Globalization;

namespace ExcelRenderer.Formulas
{
    /// <summary>Configures bounded parsing and session-wide evaluation work.</summary>
    public sealed class FormulaEvaluationOptions
    {
        /// <summary>Gets or sets the maximum formula length in UTF-16 code units.</summary>
        public int MaxFormulaLength { get; set; } = 8192;

        /// <summary>Gets or sets the maximum syntax nodes in a formula.</summary>
        public int MaxAstNodes { get; set; } = 16384;

        /// <summary>Gets or sets the maximum parser and AST depth.</summary>
        public int MaxSyntaxDepth { get; set; } = 128;

        /// <summary>Gets or sets the maximum active cell dependency depth.</summary>
        public int MaxDependencyDepth { get; set; } = 256;

        /// <summary>Gets or sets the maximum formula cells evaluated per session.</summary>
        public int MaxFormulaCells { get; set; } = 100000;

        /// <summary>Gets or sets the maximum cell reads and range visits per session.</summary>
        public int MaxCellVisits { get; set; } = 1000000;

        /// <summary>Gets or sets the maximum validation and evaluation steps per session.</summary>
        public int MaxEvaluationSteps { get; set; } = 2000000;
    }
}
