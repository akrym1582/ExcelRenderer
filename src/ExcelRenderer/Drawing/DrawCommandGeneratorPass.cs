using ExcelRenderer.Layout;
using ExcelRenderer.Model;

namespace ExcelRenderer.Drawing;

/// <summary>
/// レイアウト済みの文書から、レンダラーが使用する描画コマンドを生成します。
/// </summary>
public sealed class DrawCommandGeneratorPass
{
    private static readonly IReadOnlyList<DrawingLayer> ContinuousLayerOrder = Array.AsReadOnly(new[]
    {
        DrawingLayer.Background,
        DrawingLayer.CellBorder,
        DrawingLayer.MergedBorder,
        DrawingLayer.Text,
    });

    /// <summary>
    /// 文書内の各ページを走査し、背景、枠線、文字列、画像、および図形の描画コマンドを生成します。
    /// </summary>
    /// <param name="document">描画コマンドへ変換するレイアウト済みの文書です。</param>
    /// <returns>ページ番号と描画順序が設定された描画コマンドの一覧です。</returns>
    public IReadOnlyList<DrawCommand> Generate(RenderDocument document)
    {
        var commands = new List<DrawCommand>();
        foreach (var page in document.Pages)
        {
            commands.AddRange(GeneratePage(page));
        }

        return commands;
    }

    /// <summary>Generates commands for one page in the shared drawing order.</summary>
    /// <param name="page">The page used by this operation.</param>
    /// <returns>The planned or generated result.</returns>
    internal IReadOnlyList<DrawCommand> GeneratePage(RenderPage page)
    {
        var commands = new List<DrawCommand>();
        commands.AddRange(page.Cells.Where(x => x.Cell.Style.Background is not null)
            .Select(x => Wrap(new FillRectangleCommand(page.Number, x.Bounds, x.Cell.Style.Background!.Value), x.ClipBounds)));
        commands.AddRange(page.Cells.Where(x => x.Cell.Style.Border is not null)
            .Select(x => Wrap(new DrawBorderCommand(page.Number, x.Bounds, x.Cell.Style.Border!), x.ClipBounds)));
        commands.AddRange(page.Cells.SelectMany(cell => (cell.MergedBorders ?? []).Select(border =>
                Wrap(new DrawBorderCommand(page.Number, border.Bounds, border.Border), cell.ClipBounds))));
        commands.AddRange(page.Cells.Where(x => !string.IsNullOrEmpty(x.Cell.Text))
            .Select(x => Wrap(
                new DrawTextCommand(
                page.Number,
                GetContentBounds(x),
                x.Cell.Text!,
                x.Cell.Style)
            {
                TextLayout = x.TextLayout,
            },
                x.ClipBounds)));
        commands.AddRange((page.Images ?? []).Select(x => (Z: x.ZIndex,
                Command: (DrawCommand)new DrawImageCommand(page.Number, x.Bounds, x.ImageBytes)
                {
                    Crop = x.Crop,
                    Rotation = x.Rotation,
                    FlipHorizontal = x.FlipHorizontal,
                    FlipVertical = x.FlipVertical,
                    ClipBounds = x.ClipBounds,
                }))
            .Concat((page.Shapes ?? []).Select(x => (Z: x.Shape.ZIndex,
                Command: (DrawCommand)new DrawShapeCommand(page.Number, x.Bounds, x.Shape)
                {
                    ClipBounds = x.ClipBounds,
                })))
            .OrderBy(x => x.Z).Select(x => x.Command));
        commands.AddRange((page.HeaderFooterTexts ?? [])
            .Select(x => (DrawCommand)new DrawTextCommand(page.Number, x.Bounds, x.Text, x.Style)));

        return commands;
    }

    /// <summary>Rescans canvas cells by drawing layer without retaining all commands.</summary>
    /// <param name="plan">The plan used by this operation.</param>
    /// <param name="measurer">The measurer used by this operation.</param>
    /// <returns>The planned or generated result.</returns>
    internal IEnumerable<DrawCommand> GenerateContinuous(ContinuousLayoutPlan plan, ExcelRenderer.Abstractions.ITextMeasurer measurer)
    {
        // Rescan cells by layer: later backgrounds must never cover earlier text.
        foreach (var layer in ContinuousLayerOrder)
        {
            foreach (var cell in plan.EnumerateLayerCells(measurer, layer))
            {
                switch (layer)
                {
                    case DrawingLayer.Background:
                        if (cell.Cell.Style.Background is { } background)
                        {
                            yield return Wrap(new FillRectangleCommand(1, cell.Bounds, background), cell.ClipBounds);
                        }

                        break;
                    case DrawingLayer.CellBorder:
                        if (cell.Cell.Style.Border is { } border)
                        {
                            yield return Wrap(new DrawBorderCommand(1, cell.Bounds, border), cell.ClipBounds);
                        }

                        break;
                    case DrawingLayer.MergedBorder:
                        foreach (var merged in cell.MergedBorders ?? [])
                        {
                            yield return Wrap(new DrawBorderCommand(1, merged.Bounds, merged.Border), cell.ClipBounds);
                        }

                        break;
                    case DrawingLayer.Text:
                        var text = new DrawTextCommand(1, GetContentBounds(cell), cell.Cell.Text!, cell.Cell.Style)
                        {
                            TextLayout = cell.TextLayout,
                        };
                        yield return Wrap(text, cell.ClipBounds);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(layer));
                }
            }
        }

        foreach (var command in GeneratePage(new RenderPage(1, [], plan.Images, Shapes: plan.Shapes)))
        {
            yield return command;
        }
    }

    private static DrawCommand Wrap(DrawCommand command, ReportRect? clip) => clip is { } bounds
        ? new DrawViewportCommand(command.PageNumber, [command], bounds, 0, 0) : command;

    private static ReportRect GetContentBounds(RenderCell cell) =>
        cell.ContentBounds.Width > 0 || cell.ContentBounds.Height > 0
            ? cell.ContentBounds
            : CellContentBounds.Calculate(cell.Bounds, cell.Cell.Style);
}
