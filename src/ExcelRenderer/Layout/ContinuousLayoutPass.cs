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
                SourceAddress = layout.Address,
                ContentBounds = layout.ContentBounds,
                MergedBorders = layout.MergedBorders,
                TextLayout = context.TextLayouts.GetValueOrDefault(layout.Address),
            }).ToArray();
        var images = BuildImages(context);
        var shapes = BuildShapes(context);
        context.RenderDocument = new([new RenderPage(1, cells, images, Shapes: shapes)]);
    }

    /// <summary>Resolves images in continuous canvas coordinates.</summary>
    /// <param name="context">The context used by this operation.</param>
    /// <returns>The planned or generated result.</returns>
    internal static IReadOnlyList<RenderImage> BuildImages(ReportLayoutContext context)
    {
        context.ObjectLayouts ??= new SheetObjectLayoutIndex(context);
        return context.ObjectLayouts.Images.Select(item => new RenderImage(item.Bounds, item.Image.ImageBytes, item.Image.ZIndex)
        {
            Crop = item.Image.Crop,
            Rotation = item.Image.Rotation,
            FlipHorizontal = item.Image.FlipHorizontal,
            FlipVertical = item.Image.FlipVertical,
        }).ToArray();
    }

    /// <summary>Resolves shapes in continuous canvas coordinates.</summary>
    /// <param name="context">The context used by this operation.</param>
    /// <returns>The planned or generated result.</returns>
    internal static IReadOnlyList<RenderShape> BuildShapes(ReportLayoutContext context)
    {
        context.ObjectLayouts ??= new SheetObjectLayoutIndex(context);
        return context.ObjectLayouts.Shapes.Select(item => new RenderShape(item.Bounds, item.Shape)).ToArray();
    }
}
