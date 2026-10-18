using ExcelRenderer.Abstractions;
using ExcelRenderer.Drawing;
using ExcelRenderer.Fonts;
using ExcelRenderer.Model;
using ExcelRenderer.Rendering;
using ExcelRenderer.SkiaSharp;

namespace ExcelRenderer.Pdf;

/// <summary>Renders drawing commands to a Skia PDF with document-shared subset fonts.</summary>
public sealed class PdfRenderer : IRenderer
{
    private readonly IFontManager? fontManager;

    /// <summary>Initializes a new instance of the <see cref="PdfRenderer"/> class.</summary>
    public PdfRenderer()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PdfRenderer"/> class.</summary>
    /// <param name="fontManager">The same physical font manager used by layout.</param>
    public PdfRenderer(IFontManager fontManager) => this.fontManager = fontManager ?? throw new ArgumentNullException(nameof(fontManager));

    /// <summary>Gets the image diagnostic callback.</summary>
    public Action<ConversionDiagnostic>? DiagnosticHandler { get; init; }

    /// <inheritdoc/>
    public void Render(IReadOnlyList<DrawCommand> commands, PageSettings pageSettings, Stream output)
    {
        if (commands is null || pageSettings is null || output is null)
        {
            throw new ArgumentNullException(commands is null ? nameof(commands) : pageSettings is null ? nameof(pageSettings) : nameof(output));
        }

        using var resources = new RenderResourceSession();
        var manager = fontManager ?? new FontManager(new FontOptions());
        var measurer = new SkiaTextMeasurer(manager, cacheLayouts: true);
        var groups = commands.GroupBy(command => command.PageNumber).OrderBy(group => group.Key).Select(group => (IEnumerable<DrawCommand>)group).ToArray();
        if (groups.Length == 0)
        {
            groups = [Array.Empty<DrawCommand>()];
        }

        using var writer = new SkiaPdfDocumentWriter(manager, CancellationToken.None);
        foreach (var group in groups)
        {
            writer.Collect(PdfCommandFinalizer.Finalize(group, measurer, CancellationToken.None));
        }

        writer.Prepare();
        for (var index = 0; index < groups.Length; index++)
        {
            var descriptor = new RenderPageDescriptor(0, "commands", index + 1, index + 1, index + 1, pageSettings.Width, pageSettings.Height);
            writer.Append(pageSettings, descriptor, PdfCommandFinalizer.Finalize(groups[index], measurer, CancellationToken.None), [], diagnostic => DiagnosticHandler?.Invoke(diagnostic));
        }

        writer.Complete(output);
    }
}
