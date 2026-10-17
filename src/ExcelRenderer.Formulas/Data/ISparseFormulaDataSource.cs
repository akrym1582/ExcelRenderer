using System.Collections.Generic;
using System.Threading;
using ExcelRenderer.Formulas.References;

namespace ExcelRenderer.Formulas.Data
{
    /// <summary>Optionally enumerates every stored value or formula in a rectangle without scanning missing cells.</summary>
    /// <remarks>Coordinates must be complete, unique and ordered by row then column. Include formulas returning zero, empty text or errors and cells with calculation metadata. Style-only blanks may be omitted. Values must still be read through the session.</remarks>
    public interface ISparseFormulaDataSource : IFormulaDataSource
    {
        /// <summary>Streams stored coordinates within the inclusive rectangle.</summary>
        /// <param name="range">The rectangle.</param>
        /// <param name="cancellationToken">Cooperative cancellation.</param>
        /// <returns>Complete, ordered coordinates.</returns>
        IEnumerable<FormulaCellAddress> EnumerateStoredCells(FormulaRange range, CancellationToken cancellationToken);
    }
}
