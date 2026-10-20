using ExcelRenderer.Drawing;
using ExcelRenderer.Model;
using ExcelRenderer.Rendering;
using static ExcelRenderer.PostScript.PostScriptWriter;

namespace ExcelRenderer.PostScript;

/// <summary>Owns document-scoped resources and a bounded spool; never owns the caller's output.</summary>
internal sealed class PostScriptDocumentWriter : IDisposable
{
    private readonly PostScriptFontCollection fonts;
    private readonly SpillableBufferStream buffer;
    private readonly PostScriptWriter writer;
    private readonly CancellationToken token;
    private int pages;
    private double width;
    private double height;
    private bool prepared;
    private bool completed;

    /// <summary>Initializes a new instance of the <see cref="PostScriptDocumentWriter"/> class.</summary>
    /// <param name="buffering">The bounded spool policy.</param>
    /// <param name="token">The conversion token.</param>
    internal PostScriptDocumentWriter(RenderBufferOptions buffering, CancellationToken token)
    {
        buffering.Validate();
        this.token = token;
        fonts = new(token);
        buffer = new(buffering);
        writer = new(buffer, token);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            writer.Dispose();
        }
        finally
        {
            buffer.Dispose();
        }
    }

    /// <summary>Collects fonts and rejects unsupported transparency before emitting any output.</summary>
    /// <param name="commands">One finalized page, enumerated once.</param>
    internal void Collect(IEnumerable<DrawCommand> commands)
    {
        if (prepared)
        {
            throw new InvalidOperationException("Cannot collect after font preparation.");
        }

        var drawing = new PostScriptDrawingContext(fonts, null, token);
        foreach (var command in commands)
        {
            drawing.Execute(command);
        }
    }

    /// <summary>Prepares shared subsets and writes document resources.</summary>
    internal void Prepare()
    {
        if (prepared)
        {
            throw new InvalidOperationException("Document is already prepared.");
        }

        fonts.Prepare();
        writer.Line("%!PS-Adobe-3.0");
        writer.Line("%%Creator: ExcelRenderer");
        writer.Line("%%LanguageLevel: 3");
        writer.Line("% Requires Type 42 and PostScript 3011+ large sfnt table support.");
        writer.Line("%%DocumentData: Clean7Bit");
        writer.Line("%%Pages: (atend)");
        writer.Line("%%BoundingBox: (atend)");
        writer.Line("%%DocumentSuppliedResources: (atend)");
        writer.Line("%%EndComments");
        writer.Line("%%BeginProlog");
        writer.Line("/ERDict 20 dict def ERDict begin");
        writer.Line("/rectpath {4 dict begin /h exch def /w exch def /y exch def /x exch def x y moveto w 0 rlineto 0 h rlineto w neg 0 rlineto closepath end} bind def");
        fonts.Write(writer);
        writer.Line("%%EndProlog");
        prepared = true;
    }

    /// <summary>Writes a page with its own paper size and balanced graphics state.</summary>
    /// <param name="settings">The selected output size in points.</param>
    /// <param name="commands">The finalized page commands.</param>
    internal void Append(PageSettings settings, IEnumerable<DrawCommand> commands)
    {
        if (!prepared || completed)
        {
            throw new InvalidOperationException("Document must be prepared and incomplete.");
        }

        if (settings.Width <= 0 || settings.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(settings));
        }

        width = Math.Max(width, settings.Width);
        height = Math.Max(height, settings.Height);
        writer.Line($"%%Page: {++pages} {pages}");
        writer.Line($"%%PageBoundingBox: 0 0 {Number(Math.Ceiling(settings.Width))} {Number(Math.Ceiling(settings.Height))}");
        writer.Line("%%BeginPageSetup");
        writer.Line($"<< /PageSize [{Number(settings.Width)} {Number(settings.Height)}] >> setpagedevice");
        writer.Line($"gsave 0 {Number(settings.Height)} translate 1 -1 scale");
        writer.Line("%%EndPageSetup");
        var drawing = new PostScriptDrawingContext(fonts, writer, token, pages);
        foreach (var command in commands)
        {
            drawing.Execute(command);
        }

        writer.Line("grestore showpage");
        writer.Line("%%PageTrailer");
    }

    /// <summary>Copies a complete document to any writable stream without seeking or closing it.</summary>
    /// <param name="output">The borrowed output.</param>
    internal void Complete(Stream output)
    {
        if (!prepared || completed)
        {
            throw new InvalidOperationException("Document must be prepared and incomplete.");
        }

        writer.Line("end");
        writer.Line("%%Trailer");
        writer.Line($"%%Pages: {pages}");
        writer.Line($"%%BoundingBox: 0 0 {Number(Math.Ceiling(width))} {Number(Math.Ceiling(height))}");
        fonts.WriteResourceComments(writer);
        writer.Line("%%EOF");
        writer.Flush();
        token.ThrowIfCancellationRequested();
        ConversionMetrics.Report("postScript.bytes", buffer.Length);
        ConversionMetrics.Report("postScript.bufferMemoryPeakBytes", buffer.PeakMemoryCapacity);
        ConversionMetrics.Report("postScript.spillCount", buffer.HasSpilled ? 1 : 0);
        buffer.Position = 0;
        using var cancellable = new Core.Rendering.CancellationWriteStream(output, token);
        buffer.CopyTo(cancellable);
        token.ThrowIfCancellationRequested();
        completed = true;
    }
}
