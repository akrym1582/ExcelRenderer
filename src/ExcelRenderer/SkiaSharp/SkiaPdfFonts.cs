using System.Text;
using ExcelRenderer.Fonts;
using SkiaSharp;

namespace ExcelRenderer.SkiaSharp;

/// <summary>Adapts full Unicode and font policy to the shared document subset primitives.</summary>
internal sealed class SkiaPdfFonts : IDisposable
{
    private readonly ConversionFontResources? ownedResources;
    private readonly Core.Pdf.PdfFontCollection fonts;
    private readonly CancellationToken token;

    /// <summary>Initializes a new instance of the <see cref="SkiaPdfFonts"/> class.</summary>
    /// <param name="token">The conversion token.</param>
    /// <param name="fullEmbed">Comparison-only full embedding control.</param>
    internal SkiaPdfFonts(CancellationToken token, bool fullEmbed = false)
    {
        ownedResources = ConversionFontResources.Current is null ? new ConversionFontResources(token) : null;
        fonts = new(token, fullEmbed);
        this.token = token;
    }

    /// <summary>Gets a value indicating whether glyphs are being collected.</summary>
    internal bool IsCollecting => fonts.IsCollecting;

    /// <inheritdoc/>
    public void Dispose()
    {
        fonts.Dispose();
        ConversionFontResources.Current?.RequestCachePurge();
        ownedResources?.Dispose();
    }

    /// <summary>Checks cancellation while visiting commands.</summary>
    internal void CheckCancellation() => token.ThrowIfCancellationRequested();

    /// <summary>Prepares subsets after collection.</summary>
    internal void Prepare() => fonts.Prepare();

    /// <summary>Collects or draws already resolved source Unicode.</summary>
    /// <param name="canvas">The target canvas.</param>
    /// <param name="run">The physical font and text.</param>
    /// <param name="size">The finalized size.</param>
    /// <param name="x">The finalized X.</param>
    /// <param name="y">The finalized baseline.</param>
    /// <param name="paint">The text paint.</param>
    /// <param name="selection">The immutable finalized glyph selection.</param>
    internal void Draw(SKCanvas canvas, TextRun run, float size, float x, float y, SKPaint paint, Core.Fonts.GlyphSequence? selection = null)
    {
        var face = ConversionFontResources.Current!.GetFace(run.Font);
        var glyphs = selection?.Glyphs.ToArray() ?? ((run.GlyphId ?? run.ColorEmojiGlyphId) is { } glyph ? new[] { glyph } : face.Typeface.GetGlyphs(run.Text));
        selection ??= new(face, glyphs, run.Text, run.SourceText, run.GlyphId is not null || run.ColorEmojiGlyphId is not null || run.MissingPrivateUseGlyph);
        if (selection.FontIdentity != face.Identity)
        {
            throw new InvalidOperationException("Finalized glyphs belong to a different physical font snapshot.");
        }

        fonts.Draw(canvas, face, glyphs, selection.Source.ToArray(), selection.Clusters.ToArray(), size, x, y, paint);
    }
}
