using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>印刷設定を適用せず、使用範囲を単一の連続キャンバスへ配置します。</summary>
public sealed class ContinuousLayoutPass : IReportLayoutPass
{
    /// <summary>計算済みのセル、画像、および図形を座標を変えずに単一ページへ配置します。</summary>
    /// <param name="context">入力シートおよび計算済みのレイアウトを保持するコンテキストです。</param>
    public void Execute(ReportLayoutContext context)
    {
        var cells = context.CellLayouts.Values
            .Select(layout => new RenderCell(context.Sheet.Cells[layout.Address], layout.Bounds)
            {
                ContentBounds = layout.ContentBounds,
                MergedBorders = layout.MergedBorders,
                TextLayout = context.TextLayouts.GetValueOrDefault(layout.Address),
            }).ToArray();
        var images = (context.Sheet.Images ?? [])
            .Where(image => DrawingAnchorResolver.TryResolve(
                context,
                image.Anchor,
                image.OffsetX,
                image.OffsetY,
                image.Width,
                image.Height,
                image.DrawingAnchor,
                out _))
            .Select(image =>
            {
                DrawingAnchorResolver.TryResolve(
                    context,
                    image.Anchor,
                    image.OffsetX,
                    image.OffsetY,
                    image.Width,
                    image.Height,
                    image.DrawingAnchor,
                    out var bounds);
                return new RenderImage(
                    bounds,
                    image.ImageBytes,
                    image.ZIndex)
                {
                    Crop = image.Crop,
                    Rotation = image.Rotation,
                    FlipHorizontal = image.FlipHorizontal,
                    FlipVertical = image.FlipVertical,
                };
            }).ToArray();
        var shapes = (context.Sheet.Shapes ?? [])
            .Where(shape => DrawingAnchorResolver.TryResolve(
                context,
                shape.Anchor,
                shape.OffsetX,
                shape.OffsetY,
                shape.Width,
                shape.Height,
                shape.DrawingAnchor,
                out _))
            .Select(shape =>
            {
                DrawingAnchorResolver.TryResolve(
                    context,
                    shape.Anchor,
                    shape.OffsetX,
                    shape.OffsetY,
                    shape.Width,
                    shape.Height,
                    shape.DrawingAnchor,
                    out var bounds);
                return new RenderShape(
                    bounds,
                    shape);
            }).ToArray();
        context.RenderDocument = new([new RenderPage(1, cells, images, Shapes: shapes)]);
    }
}
