using System;
using System.Collections.Generic;
using System.Globalization;

namespace ExcelRenderer.Formulas
{
    /// <summary>Identifies a UTF-16 span in the original formula string.</summary>
    public readonly struct SourceSpan
    {
        /// <summary>Initializes a new instance of the <see cref="SourceSpan"/> struct.</summary>
        /// <param name="start">The start.</param>
        /// <param name="length">The length.</param>
        public SourceSpan(int start, int length)
        {
            Start = start;
            Length = length;
        }

        /// <summary>Gets start.</summary>
        public int Start { get; }

        /// <summary>Gets length.</summary>
        public int Length { get; }
    }
}
