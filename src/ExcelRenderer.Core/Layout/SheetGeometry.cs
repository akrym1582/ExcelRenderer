using ExcelRenderer.Core.Model;

namespace ExcelRenderer.Core.Layout;

/// <summary>
/// Provides sheet-origin point coordinates (row and column indices are 1-based, A1 starts at 0pt) that are
/// independent of the print area, print titles and the visible row/column selection. Hidden rows and columns
/// occupy 0pt.
/// </summary>
internal sealed class SheetGeometry
{
    private readonly Axis columns;
    private readonly Axis rows;

    /// <summary>Initializes a new instance of the <see cref="SheetGeometry"/> class.</summary>
    /// <param name="sheet">The source sheet.</param>
    internal SheetGeometry(ReportSheet sheet)
        : this(
            sheet.DefaultColumnWidth,
            sheet.DefaultRowHeight,
            sheet.Columns.Select(pair => new KeyValuePair<int, double>(pair.Key, pair.Value.IsHidden ? 0 : pair.Value.Width)),
            sheet.Rows.Select(pair => new KeyValuePair<int, double>(pair.Key, pair.Value.IsHidden ? 0 : pair.Value.Height)))
    {
    }

    /// <summary>Initializes a new instance of the <see cref="SheetGeometry"/> class from neutral axis sizes.</summary>
    /// <param name="defaultColumnWidth">The default column width.</param>
    /// <param name="defaultRowHeight">The default row height.</param>
    /// <param name="columnSizes">The explicit column sizes, with hidden columns set to zero.</param>
    /// <param name="rowSizes">The explicit row sizes, with hidden rows set to zero.</param>
    internal SheetGeometry(double defaultColumnWidth, double defaultRowHeight, IEnumerable<KeyValuePair<int, double>> columnSizes, IEnumerable<KeyValuePair<int, double>> rowSizes)
    {
        columns = new(defaultColumnWidth, columnSizes);
        rows = new(defaultRowHeight, rowSizes);
    }

    /// <summary>Gets the sheet-origin X of a column's left edge.</summary>
    /// <param name="column">The 1-based column index.</param>
    /// <returns>The X coordinate in points.</returns>
    internal double ColumnStart(int column) => columns.Start(column);

    /// <summary>Gets the sheet-origin Y of a row's top edge.</summary>
    /// <param name="row">The 1-based row index.</param>
    /// <returns>The Y coordinate in points.</returns>
    internal double RowStart(int row) => rows.Start(row);

    /// <summary>Gets the first column whose half-open interval contains <paramref name="x"/>.</summary>
    /// <param name="x">The X coordinate in points.</param>
    /// <returns>The 1-based column index.</returns>
    internal int ColumnAt(double x) => columns.IndexAt(x);

    /// <summary>Gets the first row whose half-open interval contains <paramref name="y"/>.</summary>
    /// <param name="y">The Y coordinate in points.</param>
    /// <returns>The 1-based row index.</returns>
    internal int RowAt(double y) => rows.IndexAt(y);

    private sealed class Axis
    {
        private readonly double defaultSize;
        private readonly int[] indices;
        private readonly double[] differences;
        private readonly double[] ends;

        internal Axis(double defaultSize, IEnumerable<KeyValuePair<int, double>> overrides)
        {
            this.defaultSize = defaultSize;
            var sorted = overrides.Where(pair => pair.Key >= 1).OrderBy(pair => pair.Key).ToArray();
            indices = sorted.Select(pair => pair.Key).ToArray();
            differences = new double[sorted.Length + 1];
            ends = new double[sorted.Length];
            for (var i = 0; i < sorted.Length; i++)
            {
                differences[i + 1] = differences[i] + sorted[i].Value - defaultSize;
                ends[i] = (sorted[i].Key * defaultSize) + differences[i + 1];
            }
        }

        internal double Start(int index)
        {
            if (index <= 1)
            {
                return 0;
            }

            var count = Array.BinarySearch(indices, index);
            count = count >= 0 ? count : ~count;
            return ((index - 1) * defaultSize) + differences[count];
        }

        internal int IndexAt(double position)
        {
            if (position <= 0)
            {
                return 1;
            }

            // Upper bound skips every zero-width interval at the boundary.
            var low = 0;
            var high = ends.Length;
            while (low < high)
            {
                var middle = low + ((high - low) / 2);
                if (ends[middle] <= position)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            var current = low == 0 ? 1 : indices[low - 1] + 1;
            var origin = low == 0 ? 0 : ends[low - 1];
            if (low < indices.Length)
            {
                var gapEnd = ((indices[low] - 1) * defaultSize) + differences[low];
                if (defaultSize <= 0 || position >= gapEnd)
                {
                    return indices[low];
                }
            }

            return defaultSize > 0 ? current + (int)Math.Floor((position - origin) / defaultSize) : current;
        }
    }
}
