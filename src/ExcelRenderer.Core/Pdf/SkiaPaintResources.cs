using System.Runtime.CompilerServices;
using ExcelRenderer.Core.Model;
using ExcelRenderer.Core.Rendering;
using SkiaSharp;

namespace ExcelRenderer.Core.Pdf;

/// <summary>Owns two lazily created paints and at most one dash effect per renderer.</summary>
internal sealed class SkiaPaintResources : IDisposable
{
    private SKPaint? fill;
    private SKPaint? stroke;
    private SKColor fillColor;
    private SKColor strokeColor;
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
        fillColor = default;
        strokeColor = default;
    }

    /// <summary>Gets the renderer's fill paint with the current RGBA color.</summary>
    /// <param name="color">The command color.</param>
    /// <returns>The borrowed paint; temporary color changes must be restored before the next owner request.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal SKPaint Fill(ReportColor color)
    {
        var rgba = new SKColor(color.Red, color.Green, color.Blue, color.Alpha);
        var observer = ConversionMetrics.Observer;
        if (fill is null)
        {
            fill = SkiaDrawingPrimitives.CreatePaint(color, SKPaintStyle.Fill);
            fillColor = rgba;
            observer?.Invoke("paintCreated", 1);
            observer?.Invoke("paintColorSetters", 1);
        }
        else if (fillColor != rgba)
        {
            fill.Color = rgba;
            fillColor = rgba;
            observer?.Invoke("paintColorSetters", 1);
        }
        else
        {
            observer?.Invoke("paintColorSettersSkipped", 1);
        }

        // Style, cap/join and antialias stay fixed after construction; only RGBA changes.
        observer?.Invoke("paintFillRequests", 1);
        return fill;
    }

    /// <summary>Gets the renderer's stroke paint with isolated width, color and dash state.</summary>
    /// <param name="side">The finalized border style.</param>
    /// <returns>The borrowed paint, valid until the next stroke.</returns>
    internal SKPaint Stroke(BorderSide side)
    {
        var color = side.Color ?? new(0, 0, 0);
        var rgba = new SKColor(color.Red, color.Green, color.Blue, color.Alpha);
        var observer = ConversionMetrics.Observer;
        if (stroke is null)
        {
            stroke = SkiaDrawingPrimitives.CreatePaint(color, SKPaintStyle.Stroke);
            strokeColor = rgba;
            observer?.Invoke("paintCreated", 1);
            observer?.Invoke("paintColorSetters", 1);
        }
        else if (strokeColor != rgba)
        {
            stroke.Color = rgba;
            strokeColor = rgba;
            observer?.Invoke("paintColorSetters", 1);
        }
        else
        {
            observer?.Invoke("paintColorSettersSkipped", 1);
        }

        observer?.Invoke("paintStrokeRequests", 1);
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
                observer?.Invoke("pathEffectCreated", 1);
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
