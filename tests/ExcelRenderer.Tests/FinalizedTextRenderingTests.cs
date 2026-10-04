using ExcelRenderer.Abstractions;
using ExcelRenderer.Drawing;
using ExcelRenderer.Fonts;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using ExcelRenderer.PdfSharp;
using ExcelRenderer.SkiaSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.IO;
using SkiaSharp;
using Xunit;

namespace ExcelRenderer.Tests;

/// <summary>Verifies finalized text state at the real PDF and Skia drawing boundaries.</summary>
public sealed class FinalizedTextRenderingTests
{
    private static readonly PageSettings Page = new(120, 80);

    /// <summary>The no-manager compatibility boundary draws with the same selected face used for measurement.</summary>
    /// <param name="bold">Whether the requested system face is bold.</param>
    /// <param name="italic">Whether the requested system face is italic.</param>
    /// <param name="shrink">Whether the measured face is shrunk and centered.</param>
    /// <param name="asPaths">Whether the selected face is rendered through glyph paths.</param>
    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, true)]
    [InlineData(true, true, true, true)]
    public void Skia_without_font_manager_draws_the_measured_system_face(
        bool bold,
        bool italic,
        bool shrink,
        bool asPaths)
    {
        const string text = "iiiiWWWW";
        var fontData = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "NotoSansJP-Regular.ttf"));
        FontStyle? selectedStyle = null;
        SKTypeface SelectTypeface(FontStyle style)
        {
            selectedStyle = style;
            return SKTypeface.FromStream(new MemoryStream(fontData, writable: false))!;
        }

        var bounds = new ReportRect(10, 20, shrink ? 35 : 100, 40);
        var style = CellStyle.Default with
        {
            Font = new("Controlled test face", 18, Bold: bold, Italic: italic),
            HorizontalAlignment = shrink ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            ShrinkToFit = shrink,
        };
        var command = new DrawTextCommand(1, bounds, text, style);
        using var actual = new SKBitmap(120, 80);
        using var actualCanvas = new SKCanvas(actual);
        actualCanvas.Clear(SKColors.White);

        new SkiaTextDrawing(asPaths, fontManager: null, SelectTypeface).PaintLegacy(actualCanvas, command);

        using var expected = new SKBitmap(120, 80);
        using var canvas = new SKCanvas(expected);
        using var typeface = SKTypeface.FromStream(new MemoryStream(fontData, writable: false));
        using var font = new SKFont(typeface, 18);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        canvas.Clear(SKColors.White);
        var width = font.MeasureText(text, paint);
        if (shrink && width > bounds.Width)
        {
            font.Size *= (float)(bounds.Width / width);
            width = font.MeasureText(text, paint);
            canvas.ClipRect(new SKRect(10, 20, 45, 60));
        }

        var x = shrink ? (float)(bounds.X + ((bounds.Width - width) / 2)) : (float)bounds.X;
        if (asPaths)
        {
            using var path = font.GetTextPath(text, new SKPoint(x, (float)bounds.Y - font.Metrics.Ascent));
            canvas.DrawPath(path, paint);
        }
        else
        {
            canvas.DrawText(text, x, (float)bounds.Y - font.Metrics.Ascent, SKTextAlign.Left, font, paint);
        }

        Assert.Equal(bold, selectedStyle!.Bold);
        Assert.Equal(italic, selectedStyle.Italic);
        AssertBitmapsEqual(expected, actual);
    }

    /// <summary>The public PNG compatibility path remains usable without a font manager.</summary>
    [Fact]
    public void Png_without_font_manager_renders_legacy_text() =>
        Assert.True(RenderInk(new DrawTextCommand(
            1,
            new(10, 20, 100, 40),
            "Legacy text",
            CellStyle.Default with { Font = new("Missing test family", 18, Bold: true, Italic: true) })) > 0);

    /// <summary>The public layout result records its measured size, including for empty text.</summary>
    [Fact]
    public void Layout_records_effective_font_size_for_text_and_empty_text()
    {
        var measurer = new PdfSharpTextMeasurer();
        var style = new FontStyle("Noto Sans JP", 12);

        Assert.Equal(12, measurer.Layout("ABC", style, 100, false).EffectiveFontSize);
        Assert.Equal(12, measurer.Layout(string.Empty, style, 100, false).EffectiveFontSize);
    }

    /// <summary>A legacy unspecified size draws with the style size, while an explicit zero remains blank.</summary>
    [Fact]
    public void Skia_distinguishes_legacy_unspecified_size_from_explicit_zero()
    {
        var line = new TextLayoutLine("ABC", 30, 14, 11, [], false);
        var legacy = new TextLayoutResult(new(30, 14), [line]);
        var zero = legacy with { EffectiveFontSize = 0 };

        Assert.True(RenderInk(CreateCommand(legacy)) > 0);
        Assert.Equal(0, RenderInk(CreateCommand(zero)));
    }

    /// <summary>Explicit run X values and line-relative baselines reach the Skia canvas unchanged.</summary>
    [Fact]
    public void Skia_draws_artificial_runs_at_finalized_offsets()
    {
        var font = MemoryFont();
        var layout = new TextLayoutResult(
            new(40, 30),
            [
                new("AB", 40, 10, 3,
                    [new(new("A", font), 0, 5), new(new("B", font), 25, 5)], false),
                new("C", 20, 20, 17, [new(new("C", font), 7, 5)], false),
            ])
        {
            EffectiveFontSize = 9,
        };
        using var output = Render(CreateCommand(layout));
        using var bitmap = SKBitmap.Decode(output.ToArray());

        Assert.True(HasInk(bitmap, 9, 18, 12, 26));
        Assert.True(HasInk(bitmap, 34, 43, 12, 26));
        Assert.True(HasInk(bitmap, 16, 25, 35, 51));
        Assert.False(HasInk(bitmap, 21, 31, 12, 26));
    }

    /// <summary>Populated finalized runs never resolve the unrelated command-style primary face.</summary>
    [Fact]
    public void Skia_finalized_runs_do_not_select_an_unused_primary_face()
    {
        var font = MemoryFont();
        var layout = new TextLayoutResult(
            new(20, 14),
            [new("A", 20, 14, 11, [new(new("A", font), 0, 10)], false)])
        {
            EffectiveFontSize = 12,
        };
        using var bitmap = new SKBitmap(120, 80);
        using var canvas = new SKCanvas(bitmap);
        var drawing = new SkiaTextDrawing(
            textAsPaths: false,
            fontManager: null,
            _ => throw new InvalidOperationException("The unused primary face was selected."));

        drawing.PaintFinalized(canvas, CreateCommand(layout), layout);

        var ink = Enumerable.Range(0, bitmap.Width).Sum(x =>
            Enumerable.Range(0, bitmap.Height).Count(y => bitmap.GetPixel(x, y).Alpha != 0));
        Assert.NotEqual(0, ink);
    }

    /// <summary>Finalized Skia clipping and optional rotation restore the caller's canvas when glyph drawing fails.</summary>
    /// <param name="rotation">The text rotation applied by the outer painter.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    public void Skia_restores_finalized_text_state_after_drawing_exception(int rotation)
    {
        using var bitmap = new SKBitmap(80, 60);
        using var canvas = new SKCanvas(bitmap);
        var initialSaveCount = canvas.SaveCount;
        var invalidRun = new TextRun("X", new("missing", 400, false, string.Empty))
        {
            GlyphId = ushort.MaxValue,
        };
        var layout = new TextLayoutResult(
            new(20, 12),
            [new("X", 20, 12, 9, [new(invalidRun, 0, 10)], false)])
        {
            EffectiveFontSize = 10,
        };
        var command = CreateCommand(layout) with
        {
            Style = CreateCommand(layout).Style with { TextRotation = rotation, WrapText = true },
        };

        Assert.Throws<InvalidOperationException>(() => new SkiaDrawingContext(true).Execute(canvas, command));
        Assert.Equal(initialSaveCount, canvas.SaveCount);

        using var paint = new SKPaint { Color = SKColors.Red };
        canvas.DrawRect(new SKRect(70, 50, 80, 60), paint);
        Assert.Equal(SKColors.Red, bitmap.GetPixel(75, 55));
    }

    /// <summary>Memory-only resolved fonts are accepted by both finalized backends and PDF text stays text.</summary>
    [Fact]
    public void Resolved_memory_face_is_used_by_pdf_and_skia()
    {
        var font = MemoryFont();
        var layout = new TextLayoutResult(
            new(20, 14),
            [new("ABC", 20, 14, 11, [new(new("ABC", font), 0, 20)], false)])
        {
            EffectiveFontSize = 12,
        };
        var command = CreateCommand(layout);
        using var pdf = new MemoryStream();

        new PdfSharpRenderer().Render([command], Page, pdf);

        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(pdf.ToArray(), 0, 5));
        pdf.Position = 0;
        using var document = PdfReader.Open(pdf, PdfDocumentOpenMode.Import);
        Assert.Contains("Tj", ContentReader.ReadContent(document.Pages[0]).ToString());
        Assert.True(RenderInk(command) > 0);
    }

    /// <summary>Finalized PDF fonts disable automatic underline because the renderer draws one manual line.</summary>
    [Fact]
    public void Finalized_pdf_font_does_not_enable_automatic_underline()
    {
        var font = PdfSharpTextMeasurer.CreateFont(
            new FontStyle("Noto Sans JP", 12, Underline: true),
            includeUnderline: false);

        Assert.False(font.Style.HasFlag(XFontStyleEx.Underline));
    }

    /// <summary>Internal PDF keys distinguish selected faces even when their family and path match.</summary>
    [Fact]
    public void Pdf_face_keys_include_identity_and_font_data()
    {
        var first = MemoryFont();
        var secondData = first.FontData!.Concat(new byte[] { 0 }).ToArray();
        var second = first with { FontData = secondData };

        var firstKey = PdfSharpFontResolver.RegisterResolvedFont(first);
        var secondKey = PdfSharpFontResolver.RegisterResolvedFont(second);
        var resolver = new PdfSharpFontResolver();

        Assert.NotEqual(firstKey, secondKey);
        Assert.Equal(first.FontData, resolver.GetFont(firstKey));
        Assert.Equal(secondData, resolver.GetFont(secondKey));
    }

    /// <summary>Registration snapshots mutable memory data and performs expensive work once under contention.</summary>
    [Fact]
    public void Pdf_face_registration_is_atomic_cached_and_immutable()
    {
        var font = MemoryFont() with { FaceId = $"parallel-{Guid.NewGuid():N}" };
        var mutableData = font.FontData!;
        var original = mutableData.ToArray();
        var before = PdfSharpFontResolver.RegistrationWork;

        var keys = Enumerable.Range(0, 32)
            .AsParallel()
            .Select(_ => PdfSharpFontResolver.RegisterResolvedFont(font))
            .ToArray();
        mutableData[0] ^= 0xff;
        var after = PdfSharpFontResolver.RegistrationWork;

        Assert.Single(keys.Distinct());
        Assert.Equal(before.Hashes + 1, after.Hashes);
        Assert.Equal(before.Reads, after.Reads);
        Assert.Equal(original, new PdfSharpFontResolver().GetFont(keys[0]));
        Assert.Equal(keys[0], PdfSharpFontResolver.RegisterResolvedFont(font));
    }

    /// <summary>The real manager/layout path reuses the selected face registration across repeated wrapping probes.</summary>
    [Fact]
    public void Text_measurer_reuses_registration_from_font_manager()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "NotoSansJP-Regular.ttf");
        var options = new FontOptions
        {
            AllowSystemFonts = false,
            UseFontPack = false,
            Registrations = [new("Measured Face", path)],
        };
        var measurer = new PdfSharpTextMeasurer(new FontManager(options));
        var before = PdfSharpFontResolver.RegistrationWork;

        _ = measurer.Layout("repeated wrapping measurement", new("Measured Face", 12), 30, true);
        var middle = PdfSharpFontResolver.RegistrationWork;
        _ = measurer.Layout("repeated wrapping measurement", new("Measured Face", 12), 30, true);
        var after = PdfSharpFontResolver.RegistrationWork;

        Assert.Equal(before.Hashes + 1, middle.Hashes);
        Assert.Equal(middle, after);
    }

    /// <summary>A file-backed face reads and hashes its immutable registration snapshot only once.</summary>
    [Fact]
    public void Pdf_file_face_registration_avoids_repeated_io_and_hashing()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "NotoSansJP-Regular.ttf");
        var font = new ResolvedFont("File Face", 400, false, path)
        {
            FaceId = $"file-{Guid.NewGuid():N}",
        };
        var before = PdfSharpFontResolver.RegistrationWork;

        var first = PdfSharpFontResolver.RegisterResolvedFont(font);
        var second = PdfSharpFontResolver.RegisterResolvedFont(font);
        var after = PdfSharpFontResolver.RegistrationWork;

        Assert.Equal(first, second);
        Assert.Equal(before.Reads + 1, after.Reads);
        Assert.Equal(before.Hashes + 1, after.Hashes);
    }

    /// <summary>Invalid explicit effective sizes are rejected at the public state boundary.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Effective_size_rejects_invalid_values(double size)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TextLayoutResult(new(0, 0), []) { EffectiveFontSize = size });
    }

    private static DrawTextCommand CreateCommand(TextLayoutResult layout) => new(
        1,
        new(10, 20, 100, 50),
        string.Concat(layout.Lines.Select(line => line.Text)),
        CellStyle.Default with { Font = new FontStyle("Noto Sans JP", 12) })
    {
        TextLayout = layout,
    };

    private static ResolvedFont MemoryFont()
    {
        var data = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "NotoSansJP-Regular.ttf"));
        return new("Same Family", 400, false, string.Empty)
        {
            FaceId = "memory-noto-regular",
            FontData = data,
        };
    }

    private static void AssertBitmapsEqual(SKBitmap expected, SKBitmap actual)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        for (var y = 0; y < expected.Height; y++)
        {
            for (var x = 0; x < expected.Width; x++)
            {
                Assert.Equal(expected.GetPixel(x, y), actual.GetPixel(x, y));
            }
        }
    }

    private static MemoryStream Render(DrawTextCommand command)
    {
        var output = new MemoryStream();
        new PngRenderer().RenderPage([command], Page, output, 72);
        output.Position = 0;
        return output;
    }

    private static int RenderInk(DrawTextCommand command)
    {
        using var output = Render(command);
        using var bitmap = SKBitmap.Decode(output.ToArray());
        return Enumerable.Range(0, bitmap.Width).Sum(x =>
            Enumerable.Range(0, bitmap.Height).Count(y => bitmap.GetPixel(x, y) != SKColors.White));
    }

    private static bool HasInk(SKBitmap bitmap, int left, int right, int top, int bottom) =>
        Enumerable.Range(left, right - left).Any(x =>
            Enumerable.Range(top, bottom - top).Any(y => bitmap.GetPixel(x, y) != SKColors.White));
}
