using ExcelRenderer.Core.Abstractions;
using ExcelRenderer.Core.Model;

namespace ExcelRenderer.Core.Layout;

/// <summary>
/// 印刷範囲を解決し、未指定の場合は A1 から内容の終端までを保持します。
/// </summary>
internal sealed class ResolvePrintAreaPass : IReportLayoutPass
{
    private const double Epsilon = 1e-6;

    /// <summary>Gets a value indicating whether an explicit print area is ignored.</summary>
    public bool IgnoreExplicitPrintArea { get; init; }

    /// <summary>
    /// 列全体・行全体の印刷範囲の未指定軸を使用範囲で制限し、自動印刷では先頭の空白行列を保持します。
    /// </summary>
    /// <param name="context">入力シート、計測機能、および各工程の計算結果を保持するレイアウトコンテキストです。</param>
    public void Execute(ReportLayoutContext context)
    {
        if (!IgnoreExplicitPrintArea && context.Sheet.PrintArea is { } explicitArea)
        {
            var wholeColumns = explicitArea.First.Row == 1 && explicitArea.Last.Row == 1048576;
            var wholeRows = explicitArea.First.Column == 1 && explicitArea.Last.Column == 16384;
            if ((wholeColumns || wholeRows) && context.Sheet.RequestedRange is null)
            {
                var ranges = context.Sheet.Cells.Select(cell => new CellRange(cell.Key, new(
                    cell.Key.Row + cell.Value.RowSpan - 1,
                    cell.Key.Column + cell.Value.ColumnSpan - 1)))
                    .Concat(context.Policy.GetObjectGeometry(context).Select(item => new CellRange(
                        new(Math.Min(item.Anchor.Row, context.Geometry.RowAt(item.Visual.Y)), Math.Min(item.Anchor.Column, context.Geometry.ColumnAt(item.Visual.X))),
                        new(
                            Math.Max(item.Anchor.Row, context.Geometry.RowAt(Math.Max(item.Visual.Y, item.Visual.Y + item.Visual.Height - Epsilon))),
                            Math.Max(item.Anchor.Column, context.Geometry.ColumnAt(Math.Max(item.Visual.X, item.Visual.X + item.Visual.Width - Epsilon)))))))
                    .Where(range => range.First.Column <= explicitArea.Last.Column && range.Last.Column >= explicitArea.First.Column &&
                        range.First.Row <= explicitArea.Last.Row && range.Last.Row >= explicitArea.First.Row)
                    .ToArray();
                explicitArea = new CellRange(
                    explicitArea.First,
                    new(
                        wholeColumns ? Math.Max(explicitArea.First.Row, ranges.Select(range => range.Last.Row).DefaultIfEmpty(1).Max()) : explicitArea.Last.Row,
                        wholeRows ? Math.Max(explicitArea.First.Column, ranges.Select(range => range.Last.Column).DefaultIfEmpty(1).Max()) : explicitArea.Last.Column));
            }

            context.PrintArea = explicitArea;
            return;
        }

        var used = context.Policy.ResolveUsedRange(context);

        // Printed worksheets retain the blank rows and columns before their first content.
        // Continuous canvases still request the tight used range explicitly.
        context.PrintArea = !IgnoreExplicitPrintArea && used is { } range
            ? new CellRange(new(Math.Min(1, range.First.Row), Math.Min(1, range.First.Column)), range.Last)
            : used;
    }

    /// <summary>Gets the get used range.</summary>
    /// <param name="sheet">The sheet used by this operation.</param>
    /// <returns>The planned or generated result.</returns>
    internal static CellRange? GetUsedRange(ReportSheet sheet)
    {
        var context = new ReportLayoutContext(sheet, new RangeOnlyMeasurer());
        return context.Policy.ResolveUsedRange(context);
    }

    /// <summary>Unions cells and neutral product object bounds using half-open object ends.</summary>
    /// <param name="sheet">The source sheet.</param>
    /// <param name="geometry">The shared original geometry.</param>
    /// <param name="objects">The product object geometry.</param>
    /// <returns>The inclusive automatic range.</returns>
    internal static CellRange? GetUsedRange(ReportSheet sheet, SheetGeometry geometry, IReadOnlyList<CoreObjectGeometry> objects)
    {
        if (sheet.Cells.Count == 0 && objects.Count == 0)
        {
            return null;
        }

        var firstRows = sheet.Cells.Keys.Select(address => address.Row)
            .Concat(objects.Select(item => Math.Min(item.Anchor.Row, geometry.RowAt(item.Visual.Y))));
        var firstColumns = sheet.Cells.Keys.Select(address => address.Column)
            .Concat(objects.Select(item => Math.Min(item.Anchor.Column, geometry.ColumnAt(item.Visual.X))));

        // Object ends are half-open: an end exactly on a boundary does not claim the next row or column.
        var lastRows = sheet.Cells.Select(cell => cell.Key.Row + cell.Value.RowSpan - 1)
            .Concat(objects.Select(item => Math.Max(
                item.Anchor.Row,
                geometry.RowAt(Math.Max(item.Visual.Y, item.Visual.Y + item.Visual.Height - Epsilon)))));
        var lastColumns = sheet.Cells.Select(cell => cell.Key.Column + cell.Value.ColumnSpan - 1)
            .Concat(objects.Select(item => Math.Max(
                item.Anchor.Column,
                geometry.ColumnAt(Math.Max(item.Visual.X, item.Visual.X + item.Visual.Width - Epsilon)))));
        return new CellRange(new(firstRows.Min(), firstColumns.Min()), new(lastRows.Max(), lastColumns.Max()));
    }
}
