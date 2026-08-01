using ReportEngine.Layout;
using ReportEngine.Model;

namespace ReportEngine.Drawing;

public abstract record DrawCommand(int PageNumber);
public sealed record FillRectangleCommand(int PageNumber, ReportRect Bounds, ReportColor Color) : DrawCommand(PageNumber);
public sealed record DrawBorderCommand(int PageNumber, ReportRect Bounds, BorderStyle Border) : DrawCommand(PageNumber);
public sealed record DrawTextCommand(int PageNumber, ReportRect Bounds, string Text, CellStyle Style) : DrawCommand(PageNumber);
public sealed record DrawLineCommand(int PageNumber, double X1, double Y1, double X2, double Y2, BorderSide Style) : DrawCommand(PageNumber);
public sealed record DrawImageCommand(int PageNumber, ReportRect Bounds, byte[] ImageBytes) : DrawCommand(PageNumber);

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
            commands.AddRange((page.Images ?? [])
                .Select(x => (DrawCommand)new DrawImageCommand(page.Number, x.Bounds, x.ImageBytes)));
            commands.AddRange((page.HeaderFooterTexts ?? [])
                .Select(x => (DrawCommand)new DrawTextCommand(page.Number, x.Bounds, x.Text, x.Style)));
        }
        return commands;
    }
}
