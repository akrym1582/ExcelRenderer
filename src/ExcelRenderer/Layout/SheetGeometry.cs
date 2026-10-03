using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

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
    {
        columns = new(sheet.DefaultColumnWidth, sheet.Columns.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.IsHidden ? 0 : pair.Value.Width));
        rows = new(sheet.DefaultRowHeight, sheet.Rows.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.IsHidden ? 0 : pair.Value.Height));
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
        private readonly SortedDictionary<int, double> overrides;

        internal Axis(double defaultSize, IReadOnlyDictionary<int, double> overrides)
        {
            this.defaultSize = defaultSize;
            this.overrides = new(overrides.Where(pair => pair.Key >= 1).ToDictionary(pair => pair.Key, pair => pair.Value));
        }

        internal double Start(int index)
        {
            if (index <= 1)
            {
                return 0;
            }

            var start = (index - 1) * defaultSize;
            foreach (var pair in overrides)
            {
                if (pair.Key >= index)
                {
                    break;
                }

                start += pair.Value - defaultSize;
            }

            return start;
        }

        internal int IndexAt(double position)
        {
            if (position <= 0)
            {
                return 1;
            }

            var current = 1;
            var origin = 0d;
            foreach (var pair in overrides)
            {
                var gap = pair.Key - current;
                if (gap > 0 && defaultSize > 0 && position < origin + (gap * defaultSize))
                {
                    return current + (int)Math.Floor((position - origin) / defaultSize);
                }

                origin += gap * defaultSize;
                current = pair.Key;
                if (position < origin + pair.Value)
                {
                    return current;
                }

                origin += pair.Value;
                current++;
            }

            return defaultSize > 0 ? current + (int)Math.Floor((position - origin) / defaultSize) : current;
        }
    }
}
