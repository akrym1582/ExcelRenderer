using ExcelRenderer.Core.Model;
using ExcelRenderer.Core.Rendering;
using SkiaSharp;

namespace ExcelRenderer.Core.Pdf;

/// <summary>Owns two lazily created paints and at most one dash effect per renderer.</summary>
internal sealed class SkiaPaintResources : IDisposable
{
    private SKPaint? fill;
    private SKPaint? stroke;
    private SKPathEffect? effect;
    private (BorderLineStyle Style, double Width)? effectKey;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (fill is not null)
        {
            fill.Dispose();
            fill = null;
            ConversionMetrics.Report("paintDisposed", 1);
        }

        if (stroke is not null)
        {
            stroke.Dispose();
            stroke = null;
            ConversionMetrics.Report("paintDisposed", 1);
        }

        DisposeEffect();
        effectKey = null;
    }

    /// <summary>Gets the renderer's fill paint with the current RGBA color.</summary>
    /// <param name="color">The command color.</param>
    /// <returns>The borrowed paint; never mutated outside this owner.</returns>
    internal SKPaint Fill(ReportColor color)
    {
        if (fill is null)
        {
            fill = SkiaDrawingPrimitives.CreatePaint(color, SKPaintStyle.Fill);
            ConversionMetrics.Report("paintCreated", 1);
        }

        // Style, cap/join and antialias stay fixed after construction; only RGBA changes.
        ConversionMetrics.Report("paintFillRequests", 1);
        fill.Color = new(color.Red, color.Green, color.Blue, color.Alpha);
        return fill;
    }

    /// <summary>Gets the renderer's stroke paint with isolated width, color and dash state.</summary>
    /// <param name="side">The finalized border style.</param>
    /// <returns>The borrowed paint, valid until the next stroke.</returns>
    internal SKPaint Stroke(BorderSide side)
    {
        if (stroke is null)
        {
            stroke = SkiaDrawingPrimitives.CreatePaint(new(0, 0, 0), SKPaintStyle.Stroke);
            ConversionMetrics.Report("paintCreated", 1);
        }

        ConversionMetrics.Report("paintStrokeRequests", 1);
        var color = side.Color ?? new(0, 0, 0);
        stroke.Color = new(color.Red, color.Green, color.Blue, color.Alpha);
        stroke.StrokeWidth = (float)side.Width;
        var key = (side.LineStyle, side.Width);
        if (effectKey != key)
        {
            stroke.PathEffect = null;
            DisposeEffect();
            effect = SkiaDrawingPrimitives.CreateDashEffect(side);
            effectKey = key;
            stroke.PathEffect = effect;
            if (effect is not null)
            {
                ConversionMetrics.Report("pathEffectCreated", 1);
            }
        }

        return stroke;
    }

    private void DisposeEffect()
    {
        if (effect is not null)
        {
            effect.Dispose();
            effect = null;
            ConversionMetrics.Report("pathEffectDisposed", 1);
        }
    }
}
