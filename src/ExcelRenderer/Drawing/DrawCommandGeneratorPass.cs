using ExcelRenderer.Layout;
using ExcelRenderer.Model;

namespace ExcelRenderer.Drawing;

/// <summary>
/// レイアウト済みの文書から、レンダラーが使用する描画コマンドを生成します。
/// </summary>
public sealed class DrawCommandGeneratorPass
{
    private const double CellTextPadding = 0.5;

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
            commands.AddRange(page.Cells.Where(x => x.Cell.Style.Background is not null)
                .Select(x => (DrawCommand)new FillRectangleCommand(page.Number, x.Bounds, x.Cell.Style.Background!.Value)));
            commands.AddRange(page.Cells.Where(x => x.Cell.Style.Border is not null)
                .Select(x => (DrawCommand)new DrawBorderCommand(page.Number, x.Bounds, x.Cell.Style.Border!)));
            commands.AddRange(page.Cells.SelectMany(cell => cell.MergedBorders ?? [])
                .Select(border => (DrawCommand)new DrawBorderCommand(page.Number, border.Bounds, border.Border)));
            commands.AddRange(page.Cells.Where(x => !string.IsNullOrEmpty(x.Cell.Text))
                .Select(x => (DrawCommand)new DrawTextCommand(page.Number, InsetCellText(x.Bounds), x.Cell.Text!, x.Cell.Style)));
            commands.AddRange((page.Images ?? []).Select(x => (Z: x.ZIndex,
                    Command: (DrawCommand)new DrawImageCommand(page.Number, x.Bounds, x.ImageBytes)))
                .Concat((page.Shapes ?? []).Select(x => (Z: x.Shape.ZIndex,
                    Command: (DrawCommand)new DrawShapeCommand(page.Number, x.Bounds, x.Shape))))
                .OrderBy(x => x.Z).Select(x => x.Command));
            commands.AddRange((page.HeaderFooterTexts ?? [])
                .Select(x => (DrawCommand)new DrawTextCommand(page.Number, x.Bounds, x.Text, x.Style)));
        }

        return commands;
    }

    private static ReportRect InsetCellText(ReportRect bounds)
    {
        var horizontalPadding = Math.Min(CellTextPadding, bounds.Width / 2);
        var verticalPadding = Math.Min(CellTextPadding, bounds.Height / 2);
        return new(bounds.X + horizontalPadding, bounds.Y + verticalPadding,
            bounds.Width - (horizontalPadding * 2), bounds.Height - (verticalPadding * 2));
    }
}
