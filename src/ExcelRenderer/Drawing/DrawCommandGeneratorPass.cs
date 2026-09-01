using ExcelRenderer.Layout;
using ExcelRenderer.Model;

namespace ExcelRenderer.Drawing;

public sealed class DrawCommandGeneratorPass
{
    public IReadOnlyList<DrawCommand> Generate(RenderDocument document)
    {
        var commands = new List<DrawCommand>();
        foreach (var page in document.Pages)
        {
            commands.AddRange(page.Cells.Where(x => x.Cell.Style.Background is not null)
                .Select(x => (DrawCommand)new FillRectangleCommand(page.Number, x.Bounds, x.Cell.Style.Background!.Value)));
            commands.AddRange(page.Cells.Where(x => x.Cell.Style.Border is not null)
                .Select(x => (DrawCommand)new DrawBorderCommand(page.Number, x.Bounds, x.Cell.Style.Border!)));
            commands.AddRange(page.Cells.Where(x => !string.IsNullOrEmpty(x.Cell.Text))
                .Select(x => (DrawCommand)new DrawTextCommand(page.Number, x.Bounds, x.Cell.Text!, x.Cell.Style)));
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
}
