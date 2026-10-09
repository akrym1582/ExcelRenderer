using ExcelRenderer.Layout;
using ExcelRenderer.Model;

namespace ExcelRenderer.CoreIntegration;

/// <summary>Materializes compatibility results only for the current page.</summary>
internal static class CorePageAdapter
{
    /// <summary>Restores the durable public page without copying image/font bytes.</summary>
    /// <param name="page">The common page.</param>
    /// <returns>The public page.</returns>
    internal static RenderPage ToPublic(Core.Layout.RenderPage page)
    {
        var styles = new Excel.StylePool();
        var data = page.ExtensionData as FullPageData;
        var cells = page.Cells.Select(cell => new RenderCell(CoreModelAdapter.ToPublic(cell.Cell, styles), CoreModelAdapter.ToPublic(cell.Bounds))
        {
            SourceAddress = cell.SourceAddress is { } address ? CoreModelAdapter.ToPublic(address) : null,
            ContentBounds = CoreModelAdapter.ToPublic(cell.ContentBounds),
            ClipBounds = cell.ClipBounds is { } clip ? CoreModelAdapter.ToPublic(clip) : null,
            TextLayout = cell.TextLayout is { } layout ? CoreTextLayoutAdapter.ToPublic(layout) : null,
            MergedBorders = cell.MergedBorders?.Select(border => new RenderBorder(CoreModelAdapter.ToPublic(border.Bounds), styles.Intern(CoreModelAdapter.ToPublic(border.Border)))).ToArray(),
        }).ToArray();
        var images = page.Images?.Select(image => image.ExtensionData is FullRenderImageData full ? full.Image : new RenderImage(CoreModelAdapter.ToPublic(image.Bounds), image.ImageBytes, image.ZIndex)).ToArray();
        var headers = page.HeaderFooterTexts?.Select(text => new RenderText(CoreModelAdapter.ToPublic(text.Bounds), text.Text, styles.Intern(CoreModelAdapter.ToPublic(text.Style)))).ToArray();
        return new(page.Number, cells, images, headers, data?.Shapes)
        {
            SourceRegions = data?.Regions ?? [],
        };
    }

    /// <summary>Projects a single public cell for shared drawing.</summary>
    /// <param name="cell">The placed public cell.</param>
    /// <returns>The neutral placed cell.</returns>
    internal static Core.Layout.RenderCell ToCore(RenderCell cell) => new(
        new Core.Model.ReportCell(cell.Cell.Text, CoreModelAdapter.ToCore(cell.Cell.Style), cell.Cell.RowSpan, cell.Cell.ColumnSpan, cell.Cell.Formula),
        CoreModelAdapter.ToCore(cell.Bounds))
    {
        ContentBounds = CoreModelAdapter.ToCore(cell.ContentBounds),
        ClipBounds = cell.ClipBounds is { } clip ? CoreModelAdapter.ToCore(clip) : null,
        TextLayout = cell.TextLayout is { } text ? CoreTextLayoutAdapter.ToCore(text) : null,
        MergedBorders = cell.MergedBorders?.Select(border => new Core.Layout.RenderBorder(CoreModelAdapter.ToCore(border.Bounds), CoreModelAdapter.ToCore(border.Border))).ToArray(),
    };

    /// <summary>Projects one page at the public drawing boundary.</summary>
    /// <param name="page">The public page.</param>
    /// <returns>The neutral page.</returns>
    internal static Core.Layout.RenderPage ToCore(RenderPage page) => new(
        page.Number,
        page.Cells.Select(ToCore).ToArray(),
        page.Images?.Select(image => new Core.Layout.RenderImage(CoreModelAdapter.ToCore(image.Bounds), image.ImageBytes, image.ZIndex) { ExtensionData = new FullRenderImageData(image) }).ToArray(),
        page.HeaderFooterTexts?.Select(text => new Core.Layout.RenderText(CoreModelAdapter.ToCore(text.Bounds), text.Text, CoreModelAdapter.ToCore(text.Style))).ToArray())
    {
        ExtensionData = new FullPageData(page.Shapes ?? [], page.SourceRegions),
    };

    /// <summary>Maps small immutable page-band plans at the facade boundary.</summary>
    /// <param name="value">The common plan.</param>
    /// <returns>The facade plan.</returns>
    internal static PaginationPagePlan ToPublic(Core.Layout.PaginationPagePlan value) => new(
        value.Horizontal is { } horizontal ? CoreModelAdapter.ToPublic(horizontal) : null,
        value.Vertical is { } vertical ? CoreModelAdapter.ToPublic(vertical) : null,
        value.BodyColumns,
        value.BodyRows,
        value.TitleColumns,
        value.TitleRows,
        value.TitleColumnEnd,
        value.TitleRowEnd,
        value.TitleWidth,
        value.TitleHeight,
        value.Scale);

    /// <summary>Maps a public band plan for independent pass execution.</summary>
    /// <param name="value">The public plan.</param>
    /// <returns>The common plan.</returns>
    internal static Core.Layout.PaginationPagePlan ToCore(PaginationPagePlan value) => new(
        value.Horizontal is { } horizontal ? CoreModelAdapter.ToCore(horizontal) : null,
        value.Vertical is { } vertical ? CoreModelAdapter.ToCore(vertical) : null,
        value.BodyColumns,
        value.BodyRows,
        value.TitleColumns,
        value.TitleRows,
        value.TitleColumnEnd,
        value.TitleRowEnd,
        value.TitleWidth,
        value.TitleHeight,
        value.Scale);
}
