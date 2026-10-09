using ExcelRenderer.Core.Layout;
using ExcelRenderer.Core.Model;

namespace ExcelRenderer.Core.Drawing;

/// <summary>
/// レイアウト済みの文書から、レンダラーが使用する描画コマンドを生成します。
/// </summary>
internal sealed class DrawCommandGeneratorPass
{
    /// <summary>Generates one shared cell layer for paged or streaming output.</summary>
    /// <param name="pageNumber">The page number.</param>
    /// <param name="cell">The placed cell.</param>
    /// <param name="layer">Background, cell border, merged border, or text.</param>
    /// <param name="requiresTextLayout">Whether finalized text geometry is mandatory.</param>
    /// <returns>The layer commands.</returns>
    internal static IEnumerable<DrawCommand> GenerateCellLayer(int pageNumber, RenderCell cell, int layer, bool requiresTextLayout)
    {
        var clip = cell.ClipBounds;
        switch (layer)
        {
            case 0:
                if (cell.Cell.Style.Background is { } background)
                {
                    yield return new FillRectangleCommand(pageNumber, cell.Bounds, background) { ClipBounds = clip };
                }

                break;
            case 1:
                if (cell.Cell.Style.Border is { } border)
                {
                    yield return new DrawBorderCommand(pageNumber, cell.Bounds, border) { ClipBounds = clip };
                }

                break;
            case 2:
                foreach (var fragment in cell.MergedBorders ?? [])
                {
                    yield return new DrawBorderCommand(pageNumber, fragment.Bounds, fragment.Border) { ClipBounds = clip };
                }

                break;
            case 3:
                if (!string.IsNullOrEmpty(cell.Cell.Text))
                {
                    yield return new DrawTextCommand(pageNumber, GetContentBounds(cell), cell.Cell.Text!, cell.Cell.Style)
                    {
                        TextLayout = cell.TextLayout ?? (requiresTextLayout ? throw new InvalidOperationException("Cell text layout is missing.") : null),
                        ClipBounds = clip,
                    };
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(layer));
        }
    }

    /// <summary>Generates commands for one page in the shared drawing order.</summary>
    /// <param name="page">The page used by this operation.</param>
    /// <param name="measurer">The header/footer layout service.</param>
    /// <param name="extension">The optional product drawing policy.</param>
    /// <returns>The planned or generated result.</returns>
    internal IReadOnlyList<DrawCommand> GeneratePage(RenderPage page, Abstractions.ITextLayoutService? measurer, Extensibility.ICoreDrawingExtension? extension = null)
    {
        var commands = new List<DrawCommand>();
        for (var layer = 0; layer < 4; layer++)
        {
            commands.AddRange(page.Cells.SelectMany(cell => GenerateCellLayer(page.Number, cell, layer, extension?.RequiresTextLayout ?? true)));
        }

        commands.AddRange(extension?.CreateObjectCommands(page) ?? (page.Images ?? []).OrderBy(image => image.ZIndex)
            .Select(image => (DrawCommand)new DrawImageCommand(page.Number, image.Bounds, image.ImageBytes)));
        commands.AddRange((page.HeaderFooterTexts ?? [])
            .Select(x => (DrawCommand)new DrawTextCommand(page.Number, x.Bounds, x.Text, x.Style)
            {
                TextLayout = measurer?.Layout(x.Text, x.Style.Font, x.Bounds.Width, false),
            }));

        Rendering.ConversionMetrics.Report("coreCommandBuild", 1);
        return commands;
    }

    private static ReportRect GetContentBounds(RenderCell cell) =>
        cell.ContentBounds.Width > 0 || cell.ContentBounds.Height > 0
            ? cell.ContentBounds
            : CellContentBounds.Calculate(cell.Bounds, cell.Cell.Style);
}
