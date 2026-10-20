using ExcelRenderer.Abstractions;
using ExcelRenderer.Drawing;
using ExcelRenderer.Fonts;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using ExcelRenderer.Pdf;
using SkiaSharp;

/// <summary>Small independent scenes exercise rendering features without changing layout per backend.</summary>
internal static class CompatibilityFixtures
{
    /// <summary>Creates fixed commands and individual screening regions.</summary>
    /// <param name="layout">The shared text layout service.</param>
    /// <returns>The compatibility scenes.</returns>
    internal static IEnumerable<Scene> Create(ITextLayoutService layout)
    {
        var font = new FontStyle("Noto Sans JP", 12);
        var text = new List<DrawCommand>();
        var regions = new List<Region>();
        var alignments = new[] { HorizontalAlignment.Left, HorizontalAlignment.Center, HorizontalAlignment.Right };
        var vertical = new[] { VerticalAlignment.Top, VerticalAlignment.Center, VerticalAlignment.Bottom };
        for (var index = 0; index < 9; index++)
        {
            var bounds = new ReportRect(15 + ((index % 3) * 130), 15 + ((index / 3) * 55), 120, 45);
            var style = CellStyle.Default with
            {
                Font = font with { Size = 7 + index, Bold = index % 2 == 0, Italic = index % 3 == 0, Underline = index % 3 == 1, Color = new((byte)(index * 20), 30, 120) },
                HorizontalAlignment = alignments[index % 3],
                VerticalAlignment = vertical[index / 3],
                WrapText = index % 2 == 0,
                ShrinkToFit = index % 2 == 1,
            };
            text.Add(new DrawTextCommand(1, bounds, "日本語 ABC 123 xyz", style));
            regions.Add(new($"alignment-{index}", [bounds.X, bounds.Y, bounds.X + bounds.Width, bounds.Y + bounds.Height]));
        }

        var rich = new DrawTextCommand(1, new(15, 200, 350, 50), "RED日本語BLUE", CellStyle.Default with { Font = font });
        var first = layout.Layout("RED日本語", font with { Color = new(220, 0, 0), Underline = true }, 350, false);
        var second = layout.Layout("BLUE", font with { Size = 18, Bold = true, Color = new(0, 0, 220) }, 350, false);
        var run1 = first.Lines[0].Runs.Select(run => run with { Font = font with { Color = new(220, 0, 0), Underline = true } });
        var run2 = second.Lines[0].Runs.Select(run => run with { X = run.X + first.Size.Width, Font = font with { Size = 18, Bold = true, Color = new(0, 0, 220) } });
        text.Add(rich with { TextLayout = new(new(first.Size.Width + second.Size.Width, 30), [new(rich.Text, first.Size.Width + second.Size.Width, 30, 20, run1.Concat(run2).ToArray(), false)]) { EffectiveFontSize = 12 } });
        regions.Add(new("rich-runs-underline", [15, 195, 370, 255]));
        text.Add(new DrawTextCommand(1, new(15, 275, 140, 55), "回転 ABC", CellStyle.Default with { Font = font, TextRotation = 25 }));
        text.Add(new DrawTextCommand(1, new(200, 275, 100, 80), "縦書ABC", CellStyle.Default with { Font = font, TextRotation = 255 }));
        regions.Add(new("rotated-text", [15, 275, 155, 330]));
        regions.Add(new("vertical-text", [200, 275, 300, 355]));
        yield return new("styles", CommandTextFinalizer.Finalize(text, layout, CancellationToken.None).ToArray(), regions, []);

        var borders = new List<DrawCommand>();
        regions = [];
        var kinds = Enum.GetValues<BorderLineStyle>();
        for (var index = 0; index < kinds.Length; index++)
        {
            var y = 20 + (index * 36);
            var side = new BorderSide(kinds[index] == BorderLineStyle.Hair ? .15 : 1, new(20, 30, 80), kinds[index]);
            borders.Add(new DrawBorderCommand(1, new(20, y, 150, 23), new(side, side, side, side)));
            borders.Add(new DrawLineCommand(1, 200, y + 10, 380, y + 10, side));
            regions.Add(new(kinds[index].ToString(), [15, y - 3, 390, y + 28]));
        }

        borders.Add(new FillRectangleCommand(1, new(20, 325, 60, 20), new(220, 10, 20, 128)));
        borders.Add(new FillRectangleCommand(1, new(50, 335, 60, 20), new(10, 30, 220, 128)));
        regions.Add(new("alpha-overlap", [15, 320, 115, 360]));
        yield return new("borders", borders, regions, []);

        var shapes = new List<DrawCommand>();
        regions = [];
        var shapeKinds = new[] { ShapeKind.Rectangle, ShapeKind.Ellipse, ShapeKind.RoundedRectangle, ShapeKind.WedgeRectangleCallout, ShapeKind.WedgeRoundedRectangleCallout };
        for (var index = 0; index < shapeKinds.Length; index++)
        {
            var bounds = new ReportRect(20 + ((index % 2) * 195), 20 + ((index / 2) * 105), 160, 65);
            var shape = new ReportShape(new(1, 1), 0, 0, bounds.Width, bounds.Height, shapeKinds[index], new(new(235, 240, 250), new(20, 40, 80), .5), new("図形 ABC", font, HorizontalAlignment.Center, VerticalAlignment.Center, true, 5, 3, 5, 3), 0, index);
            shapes.Add(new DrawShapeCommand(1, bounds, shape));
            regions.Add(new(shapeKinds[index].ToString(), [bounds.X - 2, bounds.Y - 2, bounds.X + bounds.Width + 2, bounds.Y + bounds.Height + 18]));
        }

        yield return new("shapes", CommandTextFinalizer.Finalize(shapes, layout, CancellationToken.None).ToArray(), regions, ["図形 ABC"]);

        using var bitmap = new SKBitmap(32, 32);
        for (var y = 0; y < 32; y++)
        {
            for (var x = 0; x < 32; x++)
            {
                bitmap.SetPixel(x, y, new((byte)(x * 7), (byte)(y * 7), 100, (byte)(x < 16 ? 128 : 255)));
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        using var jpeg = image.Encode(SKEncodedImageFormat.Jpeg, 95);
        var images = new List<DrawCommand>();
        regions = [];
        for (var index = 0; index < 6; index++)
        {
            var bounds = new ReportRect(30 + ((index % 3) * 130), 30 + ((index / 3) * 140), 80, 80);
            images.Add(new DrawImageCommand(1, bounds, index == 1 ? jpeg.ToArray() : png.ToArray())
            {
                Crop = index == 2 ? new(.2, .1, .1, .25) : null,
                Rotation = index >= 3 ? 25 : 0,
                FlipHorizontal = index == 4,
                FlipVertical = index == 5,
                ClipBounds = index >= 3 ? bounds : null,
            });
            regions.Add(new($"image-{index}", [bounds.X - 10, bounds.Y - 10, bounds.X + 90, bounds.Y + 90]));
        }

        yield return new("images", images, regions, []);
        var rotated = new ReportShape(new(1, 1), 0, 0, 140, 60, ShapeKind.RoundedRectangle, new(new(240, 210, 140), new(20, 40, 80), .5), new("回転図形", font, HorizontalAlignment.Center, VerticalAlignment.Center, true, 4, 4, 4, 4), 20, 0);
        DrawCommand[] layers =
        [
            new FillRectangleCommand(1, new(10, 10, 205, 130), new(225, 240, 250)),
            new DrawShapeCommand(1, new(35, 30, 140, 60), rotated) { ClipBounds = new(20, 20, 180, 100) },
            new DrawImageCommand(1, new(110, 60, 80, 80), png.ToArray()) { Rotation = -20, ClipBounds = new(20, 20, 180, 100) },
        ];
        var transformed = layers.Concat([new DrawViewportCommand(1, layers, new(0, 0, 205, 145), 15, 180)]);
        yield return new(
            "transforms",
            CommandTextFinalizer.Finalize(transformed, layout, CancellationToken.None).ToArray(),
            [new("rotated-clipped-layers", [5, 5, 220, 150]), new("viewport-origin-and-layers", [15, 180, 225, 330])],
            ["回転図形"]);
        yield return new("blank", [], [new("page", [0, 0, 421.123, 400.456])], []);
    }

    /// <summary>A named point-coordinate raster screening region.</summary>
    /// <param name="Name">Stable region name.</param>
    /// <param name="Bounds">Left, top, right, bottom in points.</param>
    internal sealed record Region(string Name, double[] Bounds);

    /// <summary>One immutable scene shared by both writers.</summary>
    /// <param name="Name">Stable fixture name.</param>
    /// <param name="Commands">Finalized commands.</param>
    /// <param name="Regions">Independent screening regions.</param>
    /// <param name="ExpectedText">Independent Unicode expectations.</param>
    internal sealed record Scene(string Name, IReadOnlyList<DrawCommand> Commands, IReadOnlyList<Region> Regions, string[] ExpectedText);
}
