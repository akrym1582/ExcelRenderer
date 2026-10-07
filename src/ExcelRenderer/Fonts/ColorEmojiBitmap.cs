using SkiaSharp;

namespace ExcelRenderer.Fonts;

/// <summary>Rasterizes a CBDT color glyph while keeping its advance in points.</summary>
internal sealed class ColorEmojiBitmap : IDisposable
{
    private ColorEmojiBitmap(SKImage image, SKRect bounds)
    {
        Image = image;
        Bounds = bounds;
    }

    /// <summary>Gets the rasterized emoji image.</summary>
    public SKImage Image { get; }

    /// <summary>Gets image rectangle relative to the glyph's baseline origin, in points.</summary>
    public SKRect Bounds { get; }

    /// <inheritdoc/>
    public void Dispose() => Image.Dispose();

    /// <summary>Creates a rasterized color emoji glyph.</summary>
    /// <param name="face">The font face containing the glyph.</param>
    /// <param name="glyphId">The glyph identifier.</param>
    /// <param name="size">The requested glyph size in points.</param>
    /// <returns>The rasterized color emoji.</returns>
    internal static ColorEmojiBitmap Create(ResolvedFont face, ushort glyphId, float size)
    {
        using var lease = new TypefaceLease(face);
        var typeface = lease.Typeface
            ?? throw new InvalidOperationException($"Color emoji font {face.Family} cannot be loaded.");
        using var font = new SKFont(typeface, size);
        var metrics = font.Metrics;
        var advance = font.GetGlyphWidths([glyphId])[0];
        const float scale = 4;
        var padding = Math.Max(2f, size * .25f);
        var height = metrics.Descent - metrics.Ascent + metrics.Leading;
        var pixelWidth = Math.Max(1, (int)Math.Ceiling((advance + (padding * 2)) * scale));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling((height + (padding * 2)) * scale));
        using var surface = SKSurface.Create(new SKImageInfo(pixelWidth, pixelHeight, SKColorType.Rgba8888, SKAlphaType.Premul))
            ?? throw new InvalidOperationException("Color emoji surface cannot be created.");
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(scale);
        using var builder = new SKTextBlobBuilder();
        builder.AddRun([glyphId], font, new SKPoint(padding, padding - metrics.Ascent));
        using var blob = builder.Build();
        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        canvas.DrawText(blob, 0, 0, paint);
        return new ColorEmojiBitmap(
            surface.Snapshot(),
            new SKRect(
                -padding,
                metrics.Ascent - padding,
                (pixelWidth / scale) - padding,
                (pixelHeight / scale) + metrics.Ascent - padding));
    }
}
