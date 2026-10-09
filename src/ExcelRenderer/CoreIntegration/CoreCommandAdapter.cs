using ExcelRenderer.Drawing;
using ExcelRenderer.Layout;

namespace ExcelRenderer.CoreIntegration;

/// <summary>Projects commands lazily while preserving drawing order and borrowing payloads.</summary>
internal static class CoreCommandAdapter
{
    /// <summary>Maps one command without copying image or font bytes.</summary>
    /// <param name="command">The original command.</param>
    /// <returns>The neutral command.</returns>
    internal static Core.Drawing.DrawCommand ToCore(DrawCommand command) => ToCore(command, CoreModelAdapter.ToCore, CoreModelAdapter.ToCore, CoreModelAdapter.ToCore);

    /// <summary>Materializes a public command for existing output APIs.</summary>
    /// <param name="command">The common command.</param>
    /// <returns>The public command, with its selection viewport.</returns>
    internal static DrawCommand ToPublic(Core.Drawing.DrawCommand command) => ToPublic(command, CoreModelAdapter.ToPublic, CoreModelAdapter.ToPublic, CoreModelAdapter.ToPublic);

    /// <summary>Creates a page-local style cache for common commands returned to public renderers.</summary>
    /// <returns>The mapper, borrowing all image/font payloads.</returns>
    internal static Func<Core.Drawing.DrawCommand, DrawCommand> CreatePublicProjection()
    {
        var style = Cached<Core.Model.CellStyle, Model.CellStyle>(CoreModelAdapter.ToPublic);
        var border = Cached<Core.Model.BorderStyle, Model.BorderStyle>(CoreModelAdapter.ToPublic);
        var side = Cached<Core.Model.BorderSide, Model.BorderSide>(CoreModelAdapter.ToPublic);
        return command => ToPublic(command, style, border, side);
    }

    /// <summary>Creates a page-local style cache for the full PDF boundary.</summary>
    /// <returns>The neutral mapper.</returns>
    internal static Func<DrawCommand, Core.Drawing.DrawCommand> CreateCoreProjection()
    {
        var style = Cached<Model.CellStyle, Core.Model.CellStyle>(CoreModelAdapter.ToCore);
        var border = Cached<Model.BorderStyle, Core.Model.BorderStyle>(CoreModelAdapter.ToCore);
        var side = Cached<Model.BorderSide, Core.Model.BorderSide>(CoreModelAdapter.ToCore);
        return command => ToCore(command, style, border, side);
    }

    /// <summary>Maps rectangle value metadata.</summary>
    /// <param name="bounds">The original rectangle.</param>
    /// <returns>The neutral rectangle.</returns>
    internal static Core.Layout.ReportRect ToCore(ReportRect bounds) => new(bounds.X, bounds.Y, bounds.Width, bounds.Height);

    private static Core.Drawing.DrawCommand ToCore(DrawCommand command, Func<Model.CellStyle, Core.Model.CellStyle> style, Func<Model.BorderStyle, Core.Model.BorderStyle> borderStyle, Func<Model.BorderSide, Core.Model.BorderSide> side) => command switch
    {
        FillRectangleCommand fill => new Core.Drawing.FillRectangleCommand(fill.PageNumber, ToCore(fill.Bounds), CoreModelAdapter.ToCore(fill.Color)),
        DrawBorderCommand border => new Core.Drawing.DrawBorderCommand(border.PageNumber, ToCore(border.Bounds), borderStyle(border.Border)),
        DrawLineCommand line => new Core.Drawing.DrawLineCommand(line.PageNumber, line.X1, line.Y1, line.X2, line.Y2, side(line.Style)),
        DrawTextCommand text => new Core.Drawing.DrawTextCommand(text.PageNumber, ToCore(text.Bounds), text.Text, style(text.Style)) { ExtensionData = new CommandData(text) },
        DrawImageCommand image => new Core.Drawing.DrawImageCommand(image.PageNumber, ToCore(image.Bounds), image.ImageBytes) { ExtensionData = new CommandData(image) },
        _ => new Core.Drawing.CoreExtensionCommand(command.PageNumber) { ExtensionData = new CommandData(command) },
    };

    private static DrawCommand ToPublic(Core.Drawing.DrawCommand command, Func<Core.Model.CellStyle, Model.CellStyle> style, Func<Core.Model.BorderStyle, Model.BorderStyle> borderStyle, Func<Core.Model.BorderSide, Model.BorderSide> side)
    {
        DrawCommand result = command.ExtensionData is CommandData data ? data.Command : command switch
        {
            Core.Drawing.FillRectangleCommand fill => new FillRectangleCommand(fill.PageNumber, CoreModelAdapter.ToPublic(fill.Bounds), CoreModelAdapter.ToPublic(fill.Color)),
            Core.Drawing.DrawBorderCommand border => new DrawBorderCommand(border.PageNumber, CoreModelAdapter.ToPublic(border.Bounds), borderStyle(border.Border)),
            Core.Drawing.DrawLineCommand line => new DrawLineCommand(line.PageNumber, line.X1, line.Y1, line.X2, line.Y2, side(line.Style)),
            Core.Drawing.DrawTextCommand text => new DrawTextCommand(text.PageNumber, CoreModelAdapter.ToPublic(text.Bounds), text.Text, style(text.Style)) { TextLayout = text.TextLayout is { } layout ? CoreTextLayoutAdapter.ToPublic(layout) : null },
            Core.Drawing.DrawImageCommand image => new DrawImageCommand(image.PageNumber, CoreModelAdapter.ToPublic(image.Bounds), image.ImageBytes),
            _ => throw new InvalidOperationException("Unknown internal drawing command."),
        };
        return command.ClipBounds is { } clip ? new DrawViewportCommand(command.PageNumber, [result], CoreModelAdapter.ToPublic(clip), 0, 0) : result;
    }

    private static Func<TSource, TTarget> Cached<TSource, TTarget>(Func<TSource, TTarget> convert)
        where TSource : notnull
    {
        var values = new Dictionary<TSource, TTarget>();
        return value =>
        {
            if (!values.TryGetValue(value, out var result))
            {
                result = convert(value);
                values[value] = result;
            }

            return result;
        };
    }

    /// <summary>Borrows an existing public command for the full painter.</summary>
    /// <param name="Command">The original command.</param>
    internal sealed record CommandData(DrawCommand Command) : Core.Extensibility.ICoreExtensionData;
}
