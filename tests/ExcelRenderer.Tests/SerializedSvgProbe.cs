using System.Text;
using System.Xml.Linq;
using ExcelRenderer.Drawing;
using ExcelRenderer.SkiaSharp;
using SkiaSharp;
using Svg.Skia;
using Xunit;

namespace ExcelRenderer.Tests;

internal sealed class SerializedSvgProbe : IDisposable
{
    internal byte[] Svg { get; }
    internal SKBitmap Png { get; }
    internal SKBitmap Raster { get; }

    internal SerializedSvgProbe(DrawTextCommand command)
    {
        using var svgOutput = new MemoryStream();
        new SvgRenderer(new OutputFixture.FixedManager()).RenderPage([command], OutputFixture.Page, svgOutput);
        Svg = svgOutput.ToArray();
        using var pngOutput = new MemoryStream();
        new PngRenderer(new OutputFixture.FixedManager()).RenderPage([command], OutputFixture.Page, pngOutput, 72);
        Png = SKBitmap.Decode(pngOutput.ToArray());
        var root = XDocument.Parse(Encoding.UTF8.GetString(Svg)).Root!;
        Assert.Equal("0 0 120 80", root.Attribute("viewBox")!.Value);
        Assert.Equal("120pt", root.Attribute("width")!.Value);
        Assert.Equal("80pt", root.Attribute("height")!.Value);
        // Parse the actual serialized bytes; scale its full viewport to exactly 120x80 pixels.
        using var input = new MemoryStream(Svg);
        using var svg = new SKSvg();
        var picture = svg.Load(input) ?? throw new InvalidDataException("Serialized SVG failed to load.");
        Raster = new SKBitmap(120, 80, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(Raster);
        canvas.Clear(SKColors.White);
        canvas.Scale(120 / picture.CullRect.Width, 80 / picture.CullRect.Height);
        canvas.Translate(-picture.CullRect.Left, -picture.CullRect.Top);
        canvas.DrawPicture(picture);
    }

    internal static bool Ink(SKBitmap bitmap, int x, int y) =>
        bitmap.GetPixel(x, y).Red < 250; // Same opaque white background and monochrome text in both outputs.

    internal void Compare(string name, Action? extra = null)
    {
        try
        {
            Assert.Equal(120, Png.Width);
            Assert.Equal(80, Png.Height);
            Match(Png, Raster);
            Match(Raster, Png);
            extra?.Invoke();
        }
        catch
        {
            var folder = Path.Combine(AppContext.BaseDirectory, "SampleOutputs", "Regression", name);
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "actual.svg"), Svg);
            Save(Png, Path.Combine(folder, "png.png"));
            Save(Raster, Path.Combine(folder, "svg-raster.png"));
            using var diff = new SKBitmap(120, 80);
            for (var y = 0; y < 80; y++)
            for (var x = 0; x < 120; x++)
            {
                diff.SetPixel(x, y, Ink(Png, x, y) == Ink(Raster, x, y) ? SKColors.White : SKColors.Red);
            }
            Save(diff, Path.Combine(folder, "diff.png"));
            throw;
        }
    }

    internal static SKRectI InkBounds(SKBitmap bitmap, SKRectI region)
    {
        var points = new List<SKPointI>();
        for (var y = region.Top; y < region.Bottom; y++)
        for (var x = region.Left; x < region.Right; x++)
        {
            if (Ink(bitmap, x, y)) { points.Add(new(x, y)); }
        }
        Assert.NotEmpty(points);
        return new(points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X) + 1, points.Max(p => p.Y) + 1);
    }

    private static void Match(SKBitmap source, SKBitmap target)
    {
        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
        {
            if (!Ink(source, x, y)) { continue; }
            var near = false;
            for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                var tx = x + dx; var ty = y + dy;
                if (tx >= 0 && tx < target.Width && ty >= 0 && ty < target.Height && Ink(target, tx, ty)) { near = true; }
            }
            Assert.True(near, $"Missing or extra ink more than 1px from matching ink at ({x},{y})");
        }
    }

    private static void Save(SKBitmap bitmap, string path)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, png.ToArray());
    }

    public void Dispose() { Png.Dispose(); Raster.Dispose(); }
}
