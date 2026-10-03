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
        var second = first with { FaceId = "memory-noto-second", FontData = secondData };

        var firstKey = PdfSharpFontResolver.RegisterResolvedFont(first);
        var secondKey = PdfSharpFontResolver.RegisterResolvedFont(second);
        var resolver = new PdfSharpFontResolver();

        Assert.NotEqual(firstKey, secondKey);
        Assert.Equal(first.FontData, resolver.GetFont(firstKey));
        Assert.Equal(secondData, resolver.GetFont(secondKey));
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
