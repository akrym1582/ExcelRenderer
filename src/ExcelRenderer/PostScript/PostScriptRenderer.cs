using ExcelRenderer.Abstractions;
using ExcelRenderer.Drawing;
using ExcelRenderer.Fonts;
using ExcelRenderer.Model;
using ExcelRenderer.Rendering;
using ExcelRenderer.SkiaSharp;

namespace ExcelRenderer.PostScript;

/// <summary>Directly renders vector commands to multipage PostScript with embedded static TrueType fonts.</summary>
public sealed class PostScriptRenderer : IRenderer
{
    private readonly IFontManager? fontManager;

    /// <summary>Initializes a new instance of the <see cref="PostScriptRenderer"/> class.</summary>
    public PostScriptRenderer()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PostScriptRenderer"/> class.</summary>
    /// <param name="fontManager">The font manager used by layout.</param>
    public PostScriptRenderer(IFontManager fontManager) => this.fontManager = fontManager ?? throw new ArgumentNullException(nameof(fontManager));

    /// <inheritdoc/>
    public void Render(IReadOnlyList<DrawCommand> commands, PageSettings pageSettings, Stream output) =>
        Render(commands, pageSettings, output, new RenderBufferOptions());

    /// <summary>Renders ordered pages with a bounded spool, preserving ownership of the output stream.</summary>
    /// <param name="commands">Materialized commands with finalized layouts where available.</param>
    /// <param name="pageSettings">The common paper size.</param>
    /// <param name="output">The caller-owned writable stream.</param>
    /// <param name="buffering">The intermediate spool policy.</param>
    /// <param name="cancellationToken">The conversion token.</param>
    public void Render(IReadOnlyList<DrawCommand> commands, PageSettings pageSettings, Stream output, RenderBufferOptions buffering, CancellationToken cancellationToken = default)
    {
        if (commands is null || pageSettings is null || output is null || buffering is null)
        {
            throw new ArgumentNullException(commands is null ? nameof(commands) : pageSettings is null ? nameof(pageSettings) : output is null ? nameof(output) : nameof(buffering));
        }

        if (!output.CanWrite)
        {
            throw new ArgumentException("Output must be writable.", nameof(output));
        }

        using var resources = new RenderResourceSession();
        var manager = fontManager ?? new FontManager(new FontOptions());
        var measurer = new SkiaTextMeasurer(manager, cacheLayouts: true);
        var groups = commands.GroupBy(command => command.PageNumber).OrderBy(group => group.Key).Select(group => (IEnumerable<DrawCommand>)group).ToArray();
        if (groups.Length == 0)
        {
            groups = [Array.Empty<DrawCommand>()];
        }

        using var writer = new PostScriptDocumentWriter(buffering, cancellationToken);
        try
        {
            foreach (var group in groups)
            {
                writer.Collect(CommandTextFinalizer.Finalize(group, measurer, cancellationToken));
            }

            writer.Prepare();
            foreach (var group in groups)
            {
                writer.Append(pageSettings, CommandTextFinalizer.Finalize(group, measurer, cancellationToken));
            }

            writer.Complete(output);
        }
        catch (PostScriptFailure error)
        {
            throw new ConversionException(error.Message, error, [new(error.Code, DiagnosticSeverity.Error, DiagnosticStage.Render, error.Message)], []);
        }
    }
}
