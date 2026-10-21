using ExcelRenderer.Core.Input;
using ExcelRenderer.OpenXml.Reader.Input;
using SkiaSharp;

namespace ExcelRenderer.Core.Pdf;

/// <summary>Owns the shared Skia PDF lifecycle, exact top origin and bounded seekable spool.</summary>
internal sealed class SkiaPdfDocument : IDisposable
{
    private readonly SpillableBufferStream spool;
    private readonly CancellationToken token;
    private readonly List<GeneratedPdfFinalizer.PageCorrection> pages = new();
    private SKManagedWStream? stream;
    private SKDocument? document;
    private bool closed;
    private int completing;

    /// <summary>Initializes a new instance of the <see cref="SkiaPdfDocument"/> class.</summary>
    /// <param name="buffering">The intermediate spool policy.</param>
    /// <param name="token">The cancellation token.</param>
    internal SkiaPdfDocument(InputBufferOptions buffering, CancellationToken token)
    {
        spool = InputBufferFactory.Create(buffering);
        this.token = token;
    }

    /// <summary>Gets or sets the comparison observer of the untouched native PDF.</summary>
    internal Action<Stream>? BeforeFinalize { get; set; }

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            if (!closed)
            {
                document?.Abort();
            }
        }
        finally
        {
            document?.Dispose();
            stream?.Dispose();
            spool.Dispose();
        }
    }

    /// <summary>Starts the native document after all subsets have been prepared.</summary>
    internal void Prepare()
    {
        token.ThrowIfCancellationRequested();
        stream = new SKManagedWStream(spool, false);
        document = SKDocument.CreatePdf(stream) ?? throw new InvalidOperationException("Skia could not create a PDF document.");
    }

    /// <summary>Appends exactly one page, including an empty page.</summary>
    /// <param name="page">The exact dimensions and full-policy links.</param>
    /// <param name="paint">The product command adapter.</param>
    internal void Append(GeneratedPdfFinalizer.PageCorrection page, Action<SKCanvas> paint)
    {
        token.ThrowIfCancellationRequested();
        var deviceWidth = Math.Ceiling(page.Width);
        var deviceHeight = Math.Ceiling(page.Height);
        var canvas = document!.BeginPage((float)deviceWidth, (float)deviceHeight);

        // Skia rounds its device extent to integer points. Allocate the enclosing
        // extent and retain the exact top origin without scaling or losing strips.
        canvas.Translate(0, (float)(deviceHeight - page.Height));
        canvas.ClipRect(new(0, 0, (float)page.Width, (float)page.Height));
        paint(canvas);
        document.EndPage();
        pages.Add(page);
    }

    /// <summary>Closes native output and writes each corrected object and encoded stream once.</summary>
    /// <param name="output">The caller-owned destination.</param>
    internal void Complete(Stream output)
    {
        Close();
        GeneratedPdfFinalizer.Write(spool, output, pages, token);
    }

    /// <summary>Closes native output and asynchronously writes the corrected PDF without owning the destination.</summary>
    /// <param name="output">The borrowed destination.</param>
    /// <returns>The completion of final output writes.</returns>
    internal Task CompleteAsync(Stream output)
    {
        Close();
        return GeneratedPdfFinalizer.WriteAsync(spool, output, pages, token);
    }

    private void Close()
    {
        token.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref completing, 1) != 0)
        {
            throw new InvalidOperationException("The PDF document can only be completed once.");
        }

        document!.Close();
        closed = true;
        stream!.Flush();
        spool.Position = 0;
        BeforeFinalize?.Invoke(spool);
    }
}
