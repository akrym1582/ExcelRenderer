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
                        SourceAddress = cell.SourceAddress,
                        TextLayout = cell.TextLayout ?? (requiresTextLayout ? throw new InvalidOperationException("Cell text layout is missing.") : null),
                        ClipBounds = TextOverflowPass.Intersect(clip, cell.TextClipBounds),
                    };
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(layer));
        }
    }

    /// <summary>Shares final-stage header/footer commands with metadata-preserving collection.</summary>
    /// <param name="page">The page header/footer text.</param>
    /// <param name="measurer">The optional header/footer measurer.</param>
    /// <returns>The same commands used by painting.</returns>
    internal static IEnumerable<DrawTextCommand> GenerateHeaderFooter(RenderPage page, Abstractions.ITextLayoutService? measurer) =>
        (page.HeaderFooterTexts ?? []).Select(x => new DrawTextCommand(page.Number, x.Bounds, x.Text, x.Style)
        {
            TextLayout = measurer?.Layout(x.Text, x.Style.Font, x.Bounds.Width, false),
        });

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
            if (layer == 1)
            {
                if (extension is not null)
                {
                    commands.AddRange(page.Cells.SelectMany(cell => extension.CreateCellDecorationCommands(page.Number, cell)));
                }

                commands.AddRange(BorderConflictResolver.Generate(page.Number, page.Cells.Concat(page.BorderOnlyCells)));
                continue;
            }

            if (layer == 2)
            {
                continue;
            }

            commands.AddRange(page.Cells.Where(cell => layer != 3 || (extension?.ShouldDrawCellText(cell) ?? true)).SelectMany(cell => GenerateCellLayer(page.Number, cell, layer, extension?.RequiresTextLayout ?? true)));
        }

        commands.AddRange(extension?.CreateObjectCommands(page) ?? (page.Images ?? []).OrderBy(image => image.ZIndex)
            .Select(image => (DrawCommand)new DrawImageCommand(page.Number, image.Bounds, image.ImageBytes)));
        commands.AddRange(GenerateHeaderFooter(page, measurer));

        Rendering.ConversionMetrics.Report("coreCommandBuild", 1);
        return commands;
    }

    /// <summary>Streams the same finalized text commands used by painting, without other layers.</summary>
    /// <param name="page">The text-only page payload.</param>
    /// <param name="measurer">The shared header/footer measurer.</param>
    /// <param name="extension">Optional product cell-text visibility and completion policy.</param>
    /// <param name="includeHeaderFooter">Whether to append headers and footers after cell text.</param>
    /// <returns>Cell text followed by header/footer text.</returns>
    internal IEnumerable<DrawTextCommand> GenerateText(RenderPage page, Abstractions.ITextLayoutService? measurer, Extensibility.ICoreDrawingExtension? extension = null, bool includeHeaderFooter = true)
    {
        foreach (var cell in page.Cells.Where(cell => extension?.ShouldDrawCellText(cell) ?? true))
        {
            foreach (var command in GenerateCellLayer(page.Number, cell, 3, extension?.RequiresTextLayout ?? true))
            {
                yield return (DrawTextCommand)command;
            }
        }

        if (includeHeaderFooter)
        {
            foreach (var command in GenerateHeaderFooter(page, measurer))
            {
                yield return command;
            }
        }
    }

    private static ReportRect GetContentBounds(RenderCell cell) =>
        cell.ContentBounds.Width > 0 || cell.ContentBounds.Height > 0
            ? cell.ContentBounds
            : CellContentBounds.Calculate(cell.Bounds, cell.Cell.Style);
}
