using System;
using System.Collections.Generic;

namespace ExcelRenderer.Formulas
{
    /// <summary>Defines the runtime evaluation strategy, including delayed and reference arguments.</summary>
    internal enum FunctionFamily
    {
        /// <summary>Scalar numeric coercion.</summary>
        Numeric,

        /// <summary>Reference aggregates.</summary>
        Aggregate,

        /// <summary>Logical and delayed branch evaluation.</summary>
        Logical,

        /// <summary>Aligned conditional aggregates.</summary>
        Criteria,

        /// <summary>Reference search and selection.</summary>
        Lookup,

        /// <summary>Workbook date arithmetic.</summary>
        Date,

        /// <summary>Bounded string operations.</summary>
        Text,
    }

    /// <summary>Keeps static validation and runtime function dispatch in one registry.</summary>
    internal static class FunctionRegistry
    {
        private static readonly Dictionary<string, Definition> Functions = Create();

        /// <summary>Resolves a supported function definition.</summary>
        /// <param name="name">Normalized function name.</param>
        /// <param name="definition">Resolved definition.</param>
        /// <returns>Whether the function is registered.</returns>
        internal static bool TryGet(string name, out Definition definition) => Functions.TryGetValue(name, out definition);

        private static Dictionary<string, Definition> Create()
        {
            var functions = new Dictionary<string, Definition>(StringComparer.OrdinalIgnoreCase);
            Add(functions, FunctionFamily.Aggregate, 0, 255, 1, "SUM", "COUNT", "COUNTA");
            Add(functions, FunctionFamily.Aggregate, 1, 255, 1, "MIN", "MAX", "AVERAGE", "AND", "OR");
            Add(functions, FunctionFamily.Logical, 0, 0, 1, "TRUE", "FALSE");
            Add(functions, FunctionFamily.Logical, 1, 1, 1, "NOT");
            Add(functions, FunctionFamily.Logical, 2, 3, 1, "IF");
            Add(functions, FunctionFamily.Logical, 2, 2, 1, "IFERROR");
            Add(functions, FunctionFamily.Numeric, 1, 1, 1, "ABS", "INT");
            Add(functions, FunctionFamily.Numeric, 2, 2, 1, "MOD", "ROUND", "ROUNDUP", "ROUNDDOWN");
            Add(functions, FunctionFamily.Criteria, 2, 2, 1, "COUNTIF");
            Add(functions, FunctionFamily.Criteria, 2, 3, 1, "SUMIF");
            Add(functions, FunctionFamily.Criteria, 2, 254, 2, "COUNTIFS");
            Add(functions, FunctionFamily.Criteria, 3, 255, 2, "SUMIFS");
            Add(functions, FunctionFamily.Lookup, 3, 4, 1, "VLOOKUP");
            Add(functions, FunctionFamily.Lookup, 2, 3, 1, "MATCH", "INDEX");
            Add(functions, FunctionFamily.Date, 3, 3, 1, "DATE");
            Add(functions, FunctionFamily.Date, 1, 1, 1, "YEAR", "MONTH", "DAY");
            Add(functions, FunctionFamily.Date, 2, 2, 1, "EDATE", "EOMONTH");
            Add(functions, FunctionFamily.Text, 1, 2, 1, "LEFT", "RIGHT");
            Add(functions, FunctionFamily.Text, 3, 3, 1, "MID");
            Add(functions, FunctionFamily.Text, 1, 1, 1, "LEN", "TRIM");
            Add(functions, FunctionFamily.Text, 3, 4, 1, "SUBSTITUTE");
            Add(functions, FunctionFamily.Text, 1, 255, 1, "CONCATENATE");
            Add(functions, FunctionFamily.Text, 2, 2, 1, "TEXT");
            return functions;
        }

        private static void Add(Dictionary<string, Definition> functions, FunctionFamily family, int min, int max, int stride, params string[] names)
        {
            foreach (var name in names)
            {
                functions.Add(name, new Definition(family, min, max, stride));
            }
        }

        /// <summary>Describes accepted argument counts and the evaluation strategy.</summary>
        internal readonly struct Definition
        {
            private readonly int min;
            private readonly int max;
            private readonly int stride;

            /// <summary>Initializes a new instance of the <see cref="Definition"/> struct.</summary>
            /// <param name="family">Runtime evaluation strategy.</param>
            /// <param name="min">Minimum arguments.</param>
            /// <param name="max">Maximum arguments.</param>
            /// <param name="stride">Argument group size.</param>
            internal Definition(FunctionFamily family, int min, int max, int stride)
            {
                Family = family;
                this.min = min;
                this.max = max;
                this.stride = stride;
            }

            /// <summary>Gets the runtime evaluation strategy.</summary>
            internal FunctionFamily Family { get; }

            /// <summary>Checks argument count and paired argument parity.</summary>
            /// <param name="count">Syntactic argument count.</param>
            /// <returns>Whether the count is valid.</returns>
            internal bool Accepts(int count) => count >= min && count <= max && (count - min) % stride == 0;
        }
    }
}
