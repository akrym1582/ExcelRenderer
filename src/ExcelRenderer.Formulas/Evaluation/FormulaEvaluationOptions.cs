using System;
using System.Collections.Generic;
using System.Globalization;

namespace ExcelRenderer.Formulas.Evaluation
{
    /// <summary>Configures bounded parsing and session-wide evaluation work.</summary>
    public sealed class FormulaEvaluationOptions
    {
        /// <summary>Gets or sets the date epoch, fixed when a session is created.</summary>
        public FormulaDateSystem DateSystem { get; set; } = FormulaDateSystem.Excel1900;

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

        /// <summary>Gets or sets the maximum length of one evaluated Text value in UTF-16 code units. Defaults to 32767; zero allows only empty text.</summary>
        public int MaxStringLength { get; set; } = 32767;

        /// <summary>Gets or sets the sum of Text lengths retained in the session cell cache, in UTF-16 code units. Defaults to 16777216; each distinct cell is counted, and zero permits only empty cached Text.</summary>
        public long MaxCachedStringCharacters { get; set; } = 16777216;

        /// <summary>Gets or sets the maximum validation and evaluation steps per session.</summary>
        public int MaxEvaluationSteps { get; set; } = 2000000;
    }
}
