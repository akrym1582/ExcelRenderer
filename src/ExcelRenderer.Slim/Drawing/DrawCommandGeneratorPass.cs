using ExcelRenderer.Slim.Layout;
using ExcelRenderer.Slim.Model;

namespace ExcelRenderer.Slim.Drawing;

/// <summary>
/// レイアウト済みの文書から、レンダラーが使用する描画コマンドを生成します。
/// </summary>
internal sealed class DrawCommandGeneratorPass
{
    /// <summary>Generates commands for one page in the shared drawing order.</summary>
    /// <param name="page">The page used by this operation.</param>
    /// <param name="measurer">The header/footer layout service.</param>
    /// <returns>The planned or generated result.</returns>
    internal IReadOnlyList<DrawCommand> GeneratePage(RenderPage page, Abstractions.ITextLayoutService measurer)
    {
        var commands = new List<DrawCommand>();
        commands.AddRange(page.Cells.Where(x => x.Cell.Style.Background is not null)
            .Select(x => new FillRectangleCommand(page.Number, x.Bounds, x.Cell.Style.Background!.Value)));
        commands.AddRange(page.Cells.Where(x => x.Cell.Style.Border is not null)
            .Select(x => new DrawBorderCommand(page.Number, x.Bounds, x.Cell.Style.Border!)));
        commands.AddRange(page.Cells.SelectMany(cell => (cell.MergedBorders ?? []).Select(border =>
                new DrawBorderCommand(page.Number, border.Bounds, border.Border))));
        commands.AddRange(page.Cells.Where(x => !string.IsNullOrEmpty(x.Cell.Text))
            .Select(x => new DrawTextCommand(
                page.Number,
                GetContentBounds(x),
                x.Cell.Text!,
                x.Cell.Style)
            {
                TextLayout = x.TextLayout ?? throw new InvalidOperationException("Cell text layout is missing."),
            }));
        commands.AddRange((page.Images ?? []).Select(x => (Z: x.ZIndex,
                Command: (DrawCommand)new DrawImageCommand(page.Number, x.Bounds, x.ImageBytes)))
            .OrderBy(x => x.Z).Select(x => x.Command));
        commands.AddRange((page.HeaderFooterTexts ?? [])
            .Select(x => (DrawCommand)new DrawTextCommand(page.Number, x.Bounds, x.Text, x.Style)
            {
                TextLayout = measurer.Layout(x.Text, x.Style.Font, x.Bounds.Width, false),
            }));

        return commands;
    }

    private static ReportRect GetContentBounds(RenderCell cell) =>
        cell.ContentBounds.Width > 0 || cell.ContentBounds.Height > 0
            ? cell.ContentBounds
            : CellContentBounds.Calculate(cell.Bounds, cell.Cell.Style);
}
