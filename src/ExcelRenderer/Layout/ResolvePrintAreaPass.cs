using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>
/// 明示された印刷範囲を採用し、未指定の場合はセル、画像、および図形から使用範囲を求めます。
/// </summary>
public sealed class ResolvePrintAreaPass : IReportLayoutPass
{
    private const double Epsilon = 1e-6;

    /// <summary>Gets a value indicating whether an explicit print area is ignored.</summary>
    public bool IgnoreExplicitPrintArea { get; init; }

    /// <summary>
    /// 明示された印刷範囲を採用し、未指定の場合は内容が存在するセル、画像、および図形を包含する範囲を設定します。
    /// </summary>
    /// <param name="context">入力シート、計測機能、および各工程の計算結果を保持するレイアウトコンテキストです。</param>
    public void Execute(ReportLayoutContext context)
    {
        context.PrintArea = !IgnoreExplicitPrintArea ? context.Sheet.PrintArea ?? GetUsedRange(context.Sheet) : GetUsedRange(context.Sheet);
    }

    private static CellRange? GetUsedRange(ReportSheet sheet)
    {
        var geometry = new SheetGeometry(sheet);
        var objects = (sheet.Images ?? []).Select(image =>
            {
                var rect = ObjectGeometry.GetSheetRect(
                    geometry,
                    image.Anchor,
                    image.OffsetX,
                    image.OffsetY,
                    image.Width,
                    image.Height,
                    image.DrawingAnchor);
                return (
                    Anchor: GetAnchor(geometry, image.DrawingAnchor, image.Anchor, rect),
                    Visual: ObjectGeometry.GetVisualBounds(rect, image.Rotation));
            })
            .Concat((sheet.Shapes ?? []).Select(shape =>
            {
                var rect = ObjectGeometry.GetSheetRect(
                    geometry,
                    shape.Anchor,
                    shape.OffsetX,
                    shape.OffsetY,
                    shape.Width,
                    shape.Height,
                    shape.DrawingAnchor);
                return (
                    Anchor: GetAnchor(geometry, shape.DrawingAnchor, shape.Anchor, rect),
                    Visual: ObjectGeometry.GetVisualBounds(rect, shape.Rotation));
            }))
            .ToArray();
        if (sheet.Cells.Count == 0 && objects.Length == 0)
        {
            var links = sheet.Hyperlinks.Where(link => link.SourceRange != default).Select(link => link.SourceRange).ToArray();
            return links.Length == 0 ? null : new CellRange(
                new(links.Min(link => link.First.Row), links.Min(link => link.First.Column)),
                new(links.Max(link => link.Last.Row), links.Max(link => link.Last.Column)));
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

    private static CellAddress GetAnchor(SheetGeometry geometry, DrawingAnchor? anchor, CellAddress fallback, ReportRect rect) =>
        anchor?.Kind == DrawingAnchorKind.Absolute
            ? new(geometry.RowAt(rect.Y), geometry.ColumnAt(rect.X))
            : anchor?.From ?? fallback;
}
