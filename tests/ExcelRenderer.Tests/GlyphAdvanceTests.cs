using ExcelRenderer.Core.Fonts;
using SkiaSharp;
using Xunit;

namespace ExcelRenderer.Tests;

public sealed class GlyphAdvanceTests
{
    [Theory]
    [InlineData("static.ttf")]
    [InlineData("cff.otf")]
    [InlineData("variable.ttf")]
    [InlineData("collection.ttc")]
    [InlineData("colr.ttf")]
    [InlineData("colr-v1.ttf")]
    public void Batched_advances_and_origins_preserve_individual_glyph_arithmetic(string file)
    {
        using var face = new SkiaFontFace(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fonts/PdfFormats", file)));
        var glyphs = face.Typeface.GetGlyphs("ABA あ😀 e\u0301 ");
        foreach (var size in new[] { 1d, 10.125, 24 })
        {
            var expected = glyphs.Select(glyph => face.Measure([glyph], size)).ToArray();
            Assert.Equal(expected, face.Advances(glyphs, size));
            var positions = face.Positions(glyphs, size);
            var x = 0d;
            for (var index = 0; index < glyphs.Length; index++)
            {
                Assert.Equal(new SKPoint((float)x, 0), positions[index]);
                x += expected[index];
            }
        }

        Assert.Empty(face.Advances([], 10));
        Assert.Empty(face.Positions([], 10));
    }
}
