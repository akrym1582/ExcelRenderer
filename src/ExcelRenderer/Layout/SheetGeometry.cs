using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>
/// Provides sheet-origin point coordinates (row and column indices are 1-based, A1 starts at 0pt) that are
/// independent of the print area, print titles and the visible row/column selection. Hidden rows and columns
/// occupy 0pt.
/// </summary>
internal sealed class SheetGeometry
{
    private readonly Core.Layout.SheetGeometry inner;

    /// <summary>Initializes a new instance of the <see cref="SheetGeometry"/> class.</summary>
    /// <param name="sheet">The source sheet.</param>
    internal SheetGeometry(ReportSheet sheet) => inner = new(
        sheet.DefaultColumnWidth,
        sheet.DefaultRowHeight,
        sheet.Columns.Select(pair => new KeyValuePair<int, double>(pair.Key, pair.Value.IsHidden ? 0 : pair.Value.Width)),
        sheet.Rows.Select(pair => new KeyValuePair<int, double>(pair.Key, pair.Value.IsHidden ? 0 : pair.Value.Height)));

    /// <summary>Gets the immutable common geometry used at internal boundaries.</summary>
    internal Core.Layout.SheetGeometry CoreGeometry => inner;

    /// <summary>Gets a column's sheet-origin coordinate.</summary>
    /// <param name="column">The one-based column.</param>
    /// <returns>The coordinate in points.</returns>
    internal double ColumnStart(int column) => inner.ColumnStart(column);

    /// <summary>Gets a row's sheet-origin coordinate.</summary>
    /// <param name="row">The one-based row.</param>
    /// <returns>The coordinate in points.</returns>
    internal double RowStart(int row) => inner.RowStart(row);

    /// <summary>Finds the column containing a coordinate.</summary>
    /// <param name="x">The coordinate in points.</param>
    /// <returns>The one-based column.</returns>
    internal int ColumnAt(double x) => inner.ColumnAt(x);

    /// <summary>Finds the row containing a coordinate.</summary>
    /// <param name="y">The coordinate in points.</param>
    /// <returns>The one-based row.</returns>
    internal int RowAt(double y) => inner.RowAt(y);
}
