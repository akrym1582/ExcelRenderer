using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>印刷設定を適用せず、使用範囲を単一の連続キャンバスへ配置します。</summary>
public sealed class ContinuousLayoutPass : IReportLayoutPass
{
    /// <summary>計算済みのセル、画像、および図形を座標を変えずに単一ページへ配置します。</summary>
    public void Execute(ReportLayoutContext context)
    {
        var cells = context.CellLayouts.Values
            .Select(layout => new RenderCell(context.Sheet.Cells[layout.Address], layout.Bounds)
            {
                MergedBorders = layout.MergedBorders,
            }).ToArray();
        var images = (context.Sheet.Images ?? [])
            .Where(image => context.ColumnLayouts.TryGetValue(image.Anchor.Column, out _) &&
                context.RowLayouts.TryGetValue(image.Anchor.Row, out _))
            .Select(image =>
            {
                var column = context.ColumnLayouts[image.Anchor.Column];
                var row = context.RowLayouts[image.Anchor.Row];
                return new RenderImage(
                    new(column.X + image.OffsetX, row.Y + image.OffsetY, image.Width, image.Height),
                    image.ImageBytes,
                    image.ZIndex);
            }).ToArray();
        var shapes = (context.Sheet.Shapes ?? [])
            .Where(shape => context.ColumnLayouts.TryGetValue(shape.Anchor.Column, out _) &&
                context.RowLayouts.TryGetValue(shape.Anchor.Row, out _))
            .Select(shape =>
            {
                var column = context.ColumnLayouts[shape.Anchor.Column];
                var row = context.RowLayouts[shape.Anchor.Row];
                return new RenderShape(
                    new(column.X + shape.OffsetX, row.Y + shape.OffsetY, shape.Width, shape.Height),
                    shape);
            }).ToArray();
        context.RenderDocument = new([new RenderPage(1, cells, images, Shapes: shapes)]);
    }
}
