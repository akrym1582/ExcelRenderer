using ExcelRenderer.Abstractions;
using ExcelRenderer.Drawing;
using ExcelRenderer.Fonts;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using SkiaSharp;

namespace ExcelRenderer.SkiaSharp;

/// <summary>Owns Skia font resources and finalized and compatibility run drawing.</summary>
internal sealed class SkiaTextDrawing
{
    private readonly bool _textAsPaths;
    private readonly Action? _beforeLegacyDrawing;
    private readonly IFontManager? _fontManager;
    private readonly Func<FontStyle, SKTypeface?> _systemTypefaceSelector;

    /// <summary>Initializes a new instance of the <see cref="SkiaTextDrawing"/> class.</summary>
    /// <param name="textAsPaths">Whether ordinary text is converted to owned glyph paths.</param>
    /// <param name="fontManager">The optional resolved-font source.</param>
    /// <param name="systemTypefaceSelector">The optional system-face selection boundary used by deterministic tests.</param>
    /// <param name="beforeLegacyDrawing">Optional internal observer after clipping and measurement, before compatibility run drawing.</param>
    internal SkiaTextDrawing(
        bool textAsPaths,
        IFontManager? fontManager,
        Func<FontStyle, SKTypeface?>? systemTypefaceSelector = null,
        Action? beforeLegacyDrawing = null)
    {
        _textAsPaths = textAsPaths;
        _beforeLegacyDrawing = beforeLegacyDrawing;
        _fontManager = fontManager;
        _systemTypefaceSelector = systemTypefaceSelector ?? CreateSystemTypeface;
    }

    /// <summary>Paints and measures compatibility text while owning all temporary font resources.</summary>
    /// <param name="canvas">The caller-owned canvas.</param>
    /// <param name="command">The compatibility text command.</param>
    internal void PaintLegacy(SKCanvas canvas, DrawTextCommand command)
    {
        var request = new FontRequest(
            command.Style.Font.Family,
            command.Style.Font.Bold ? 700 : 400,
            command.Style.Font.Italic);
        var resolved = _fontManager?.Resolve(request);
        using var resolvedTypeface = resolved is null ? null : CreateTypeface(resolved);
        using var systemTypeface = resolvedTypeface is null
            ? _systemTypefaceSelector(command.Style.Font)
            : null;
        var typeface = resolvedTypeface ?? systemTypeface ?? SKTypeface.Default;
        using var font = new SKFont(typeface, (float)command.Style.Font.Size);
        using var paint = CreatePaint(command.Style.Font.Color ?? new(0, 0, 0), SKPaintStyle.Fill);
        paint.IsAntialias = true;

        if (command.Style.ShrinkToFit && !command.Style.WrapText && command.Bounds.Width > 0)
        {
            var widest = command.Text.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n').Max(line => MeasureText(line, request, font, paint));
            if (widest > command.Bounds.Width)
            {
                font.Size *= (float)(command.Bounds.Width / widest);
            }
        }

        var lines = WrapText(command.Text, request, font, paint, command.Bounds.Width, command.Style.WrapText);
        var metrics = font.Metrics;
        var lineHeight = metrics.Descent - metrics.Ascent + metrics.Leading;
        var textHeight = lineHeight * lines.Count;
        var y = command.Style.VerticalAlignment switch
        {
            VerticalAlignment.Center => (float)(command.Bounds.Y + ((command.Bounds.Height - textHeight) / 2)) - metrics.Ascent,
            VerticalAlignment.Bottom => (float)(command.Bounds.Y + command.Bounds.Height - textHeight) - metrics.Ascent,
            _ => (float)command.Bounds.Y - metrics.Ascent,
        };

        canvas.Save();
        try
        {
            if (command.Style.WrapText || command.Style.ShrinkToFit)
            {
                canvas.ClipRect(ToRect(command.Bounds));
            }

            for (var index = 0; index < lines.Count; index++)
            {
                var line = lines[index];
                var lineWidth = MeasureText(line, request, font, paint);
                var x = command.Style.HorizontalAlignment switch
                {
                    HorizontalAlignment.Center => (float)(command.Bounds.X + ((command.Bounds.Width - lineWidth) / 2)),
                    HorizontalAlignment.Right => (float)(command.Bounds.X + command.Bounds.Width - lineWidth),
                    _ => (float)command.Bounds.X,
                };
                _beforeLegacyDrawing?.Invoke();
                if (_textAsPaths)
                {
                    DrawResolvedLine(canvas, line, request, typeface, font.Size, x, y, paint, asPaths: true);
                }
                else
                {
                    DrawResolvedLine(canvas, line, request, typeface, font.Size, x, y, paint, asPaths: false);
                }

                if (command.Style.Font.Underline)
                {
                    canvas.DrawLine(x, y + 1, x + (float)lineWidth, y + 1, paint);
                }

                y += lineHeight;
            }
        }
        finally
        {
            canvas.Restore();
        }
    }

    /// <summary>Paints stored lines and runs without changing finalized geometry.</summary>
    /// <param name="canvas">The caller-owned canvas.</param>
    /// <param name="command">The finalized text command.</param>
    /// <param name="layout">The finalized point-coordinate layout.</param>
    internal void PaintFinalized(SKCanvas canvas, DrawTextCommand command, TextLayoutResult layout)
    {
        var effectiveFontSize = TextLayoutFontSize.Resolve(layout, command.Style.Font);
        if (effectiveFontSize == 0)
        {
            return;
        }

        using var paint = CreatePaint(command.Style.Font.Color ?? new(0, 0, 0), SKPaintStyle.Fill);
        paint.IsAntialias = true;
        SKTypeface? selectedPrimaryTypeface = null;
        var primaryTypefaceSelected = false;
        canvas.Save();
        try
        {
            if (command.Style.WrapText || command.Style.ShrinkToFit)
            {
                canvas.ClipRect(ToRect(command.Bounds));
            }

            var request = new FontRequest(
                command.Style.Font.Family,
                command.Style.Font.Bold ? 700 : 400,
                command.Style.Font.Italic);
            foreach (var positioned in TextLayoutPlacement.Place(
                         layout,
                         command.Bounds,
                         command.Style.HorizontalAlignment,
                         command.Style.VerticalAlignment))
            {
                if (positioned.Line.Runs.Count == 0)
                {
                    if (!primaryTypefaceSelected)
                    {
                        selectedPrimaryTypeface = SelectPrimaryTypeface(command.Style.Font, request);
                        primaryTypefaceSelected = true;
                    }

                    DrawResolvedLine(
                        canvas,
                        positioned.Line.Text,
                        request,
                        selectedPrimaryTypeface ?? SKTypeface.Default,
                        (float)effectiveFontSize,
                        (float)positioned.Left,
                        (float)positioned.Baseline,
                        paint,
                        _textAsPaths);
                }
                else
                {
                    foreach (var run in positioned.Line.Runs)
                    {
                        DrawResolvedRun(
                            canvas,
                            run.Run,
                            (float)effectiveFontSize,
                            (float)(positioned.Left + run.X),
                            (float)positioned.Baseline,
                            paint,
                            _textAsPaths);
                    }
                }

                if (command.Style.Font.Underline)
                {
                    canvas.DrawLine(
                        (float)positioned.Left,
                        (float)(positioned.Baseline + 1),
                        (float)(positioned.Left + positioned.Line.Width),
                        (float)(positioned.Baseline + 1),
                        paint);
                }
            }
        }
        finally
        {
            selectedPrimaryTypeface?.Dispose();
            canvas.Restore();
        }
    }

    private static SKTypeface? CreateTypeface(ResolvedFont font)
    {
        using Stream stream = font.FontData is null
            ? File.OpenRead(font.FilePath)
            : new MemoryStream(font.FontData, writable: false);
        return SKTypeface.FromStream(stream)
            ?? throw new InvalidDataException($"フォントデータから書体を生成できません: {font.Family}");
    }

    private static SKTypeface? CreateSystemTypeface(FontStyle style) =>
        SKTypeface.FromFamilyName(
            style.Family,
            style.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal,
            style.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);

    private static SKRect ToRect(ReportRect rect) =>
        new((float)rect.X, (float)rect.Y, (float)(rect.X + rect.Width), (float)(rect.Y + rect.Height));

    private static float MeasureRun(SKFont font, TextRun run, SKPaint paint)
    {
        if ((run.GlyphId ?? run.ColorEmojiGlyphId) is not { } glyphId)
        {
            return font.MeasureText(run.Text, paint);
        }

        var widths = font.GetGlyphWidths([glyphId]);
        return widths.Length == 0 ? 0 : widths[0];
    }

    private SKTypeface? SelectPrimaryTypeface(FontStyle style, FontRequest request)
    {
        var resolved = _fontManager?.Resolve(request);
        return resolved is null
            ? _systemTypefaceSelector(style)
            : CreateTypeface(resolved);
    }

    private IReadOnlyList<string> WrapText(string text, FontRequest request, SKFont font, SKPaint paint, double width, bool wrap)
    {
        if (!wrap || width <= 0)
        {
            return text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        }

        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (paragraph.Length == 0)
            {
                lines.Add(string.Empty);
                continue;
            }

            var line = string.Empty;
            var elements = System.Globalization.StringInfo.GetTextElementEnumerator(paragraph);
            while (elements.MoveNext())
            {
                var element = (string)elements.Current!;
                var candidate = line + element;
                if (line.Length > 0 && MeasureText(candidate, request, font, paint) > width)
                {
                    lines.Add(line);
                    line = element;
                }
                else
                {
                    line = candidate;
                }
            }

            lines.Add(line);
        }

        return lines;
    }

    private float MeasureText(string text, FontRequest request, SKFont defaultFont, SKPaint paint)
    {
        if (_fontManager is null)
        {
            return defaultFont.MeasureText(text, paint);
        }

        var width = 0f;
        foreach (var run in _fontManager.ResolveTextRuns(text, request))
        {
            using var typeface = CreateTypeface(run.Font);
            using var font = new SKFont(typeface, defaultFont.Size);
            width += MeasureRun(font, run, paint);
        }

        return width;
    }

    private void DrawResolvedLine(
        SKCanvas canvas,
        string text,
        FontRequest request,
        SKTypeface primaryTypeface,
        float size,
        float x,
        float y,
        SKPaint paint,
        bool asPaths)
    {
        var runs = _fontManager?.ResolveTextRuns(text, request);
        if (runs is null)
        {
            using var font = new SKFont(primaryTypeface, size);
            DrawUnresolvedRun(canvas, text, x, y, font, paint, asPaths);
            return;
        }

        foreach (var run in runs)
        {
            DrawResolvedRun(canvas, run, size, x, y, paint, asPaths);
            using var typeface = run.Font.FontData is null && string.IsNullOrEmpty(run.Font.FilePath)
                ? null
                : CreateTypeface(run.Font);
            using var font = new SKFont(typeface ?? SKTypeface.Default, size);
            x += MeasureRun(font, run, paint);
        }
    }

    private void DrawUnresolvedRun(
        SKCanvas canvas,
        string text,
        float x,
        float y,
        SKFont font,
        SKPaint paint,
        bool asPaths)
    {
        if (asPaths)
        {
            using var path = font.GetTextPath(text, new SKPoint(x, y));
            if (path.IsEmpty && text.Any(character => !char.IsWhiteSpace(character)))
            {
                throw new InvalidOperationException("文字のアウトラインを生成できません。");
            }

            canvas.DrawPath(path, paint);
        }
        else
        {
            canvas.DrawText(text, x, y, SKTextAlign.Left, font, paint);
        }
    }

    private void DrawResolvedRun(
        SKCanvas canvas,
        TextRun run,
        float size,
        float x,
        float y,
        SKPaint paint,
        bool asPaths)
    {
        using var typeface = run.Font.FontData is null && string.IsNullOrEmpty(run.Font.FilePath)
            ? null
            : CreateTypeface(run.Font);
        using var font = new SKFont(typeface ?? SKTypeface.Default, size);
        if (run.ColorEmojiGlyphId is { } emojiGlyph)
        {
            if (asPaths)
            {
                using var bitmap = ColorEmojiBitmap.Create(run.Font, emojiGlyph, size);
                canvas.DrawImage(
                    bitmap.Image,
                    new SKRect(
                        x + bitmap.Bounds.Left,
                        y + bitmap.Bounds.Top,
                        x + bitmap.Bounds.Right,
                        y + bitmap.Bounds.Bottom),
                    new SKSamplingOptions(SKCubicResampler.Mitchell));
            }
            else
            {
                using var builder = new SKTextBlobBuilder();
                builder.AddRun([emojiGlyph], font, new SKPoint(x, y));
                using var blob = builder.Build();
                using var emojiPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
                canvas.DrawText(blob, 0, 0, emojiPaint);
            }
        }
        else if (run.GlyphId is { } glyphId)
        {
            if (asPaths)
            {
                using var glyphPath = font.GetGlyphPath(glyphId)
                    ?? throw new InvalidOperationException($"IVS glyph {glyphId} のアウトラインを生成できません。");
                if (glyphPath.IsEmpty)
                {
                    throw new InvalidOperationException($"IVS glyph {glyphId} のアウトラインが空です。");
                }

                glyphPath.Transform(SKMatrix.CreateTranslation(x, y));
                canvas.DrawPath(glyphPath, paint);
            }
            else
            {
                using var builder = new SKTextBlobBuilder();
                builder.AddRun([glyphId], font, new SKPoint(x, y));
                using var blob = builder.Build();
                canvas.DrawText(blob, 0, 0, paint);
            }
        }
        else if (asPaths)
        {
            using var path = font.GetTextPath(run.Text, new SKPoint(x, y));
            if (path.IsEmpty && run.Text.Any(character => !char.IsWhiteSpace(character)))
            {
                throw new InvalidOperationException("文字のアウトラインを生成できません。");
            }

            canvas.DrawPath(path, paint);
        }
        else
        {
            canvas.DrawText(run.Text, x, y, SKTextAlign.Left, font, paint);
        }
    }

    private SKPaint CreatePaint(ReportColor color, SKPaintStyle style, double width = 1) => new()
    {
        Color = new SKColor(color.Red, color.Green, color.Blue, color.Alpha),
        Style = style,
        StrokeWidth = (float)width,
        IsAntialias = true,
    };
}
