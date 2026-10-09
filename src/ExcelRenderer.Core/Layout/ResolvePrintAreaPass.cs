using ExcelRenderer.Core.Abstractions;
using ExcelRenderer.Core.Model;

namespace ExcelRenderer.Core.Layout;

/// <summary>
/// 明示された印刷範囲を採用し、未指定の場合はセル、画像から使用範囲を求めます。
/// </summary>
internal sealed class ResolvePrintAreaPass : IReportLayoutPass
{
    private const double Epsilon = 1e-6;

    /// <summary>Gets a value indicating whether an explicit print area is ignored.</summary>
    public bool IgnoreExplicitPrintArea { get; init; }

    /// <summary>
    /// 明示された印刷範囲を採用し、未指定の場合は内容が存在するセル、画像を包含する範囲を設定します。
    /// </summary>
    /// <param name="context">入力シート、計測機能、および各工程の計算結果を保持するレイアウトコンテキストです。</param>
    public void Execute(ReportLayoutContext context)
    {
        context.PrintArea = !IgnoreExplicitPrintArea ? context.Sheet.PrintArea ?? context.Policy.ResolveUsedRange(context) : context.Policy.ResolveUsedRange(context);
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
