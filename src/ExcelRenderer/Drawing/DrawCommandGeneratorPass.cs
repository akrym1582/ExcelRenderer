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
        var generator = new Core.Drawing.DrawCommandGeneratorPass();
        return generator.GeneratePage(CoreIntegration.CorePageAdapter.ToCore(page), null, new CoreIntegration.FullDrawingExtension())
            .Select(CoreIntegration.CoreCommandAdapter.CreatePublicProjection()).ToArray();
    }

    /// <summary>Rescans canvas cells by drawing layer without retaining all commands.</summary>
    /// <param name="plan">The plan used by this operation.</param>
    /// <param name="measurer">The measurer used by this operation.</param>
    /// <returns>The planned or generated result.</returns>
    internal IEnumerable<DrawCommand> GenerateContinuous(ContinuousLayoutPlan plan, ExcelRenderer.Abstractions.ITextMeasurer measurer)
    {
        // Rescan cells by layer: later backgrounds must never cover earlier text.
        var projection = CoreIntegration.CoreCommandAdapter.CreatePublicProjection();
        foreach (var layer in ContinuousLayerOrder)
        {
            foreach (var cell in plan.EnumerateLayerCells(measurer, layer))
            {
                var coreLayer = layer switch
                {
                    DrawingLayer.Background => 0,
                    DrawingLayer.CellBorder => 1,
                    DrawingLayer.MergedBorder => 2,
                    DrawingLayer.Text => 3,
                    _ => throw new ArgumentOutOfRangeException(nameof(layer)),
                };
                foreach (var command in Core.Drawing.DrawCommandGeneratorPass.GenerateCellLayer(1, CoreIntegration.CorePageAdapter.ToCore(cell), coreLayer, false))
                {
                    yield return projection(command);
                }
            }
        }

        foreach (var command in GeneratePage(new RenderPage(1, [], plan.Images, Shapes: plan.Shapes)))
        {
            yield return command;
        }
    }
}
