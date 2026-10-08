using ExcelRenderer.Drawing;
using ExcelRenderer.Fonts;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using ExcelRenderer.Rendering;
using ExcelRenderer.SkiaSharp;
using SkiaSharp;
using Xunit;

namespace ExcelRenderer.Tests;

/// <summary>PNG・SVG の直接描画でネイティブ書体を共有し、正常終了と失敗時に適切に解放することを検証します。</summary>
public sealed class ImagePerformanceRegressionTests
{
    /// <summary>PNG・SVG の各直接描画 API でネイティブ書体を共有し、描画終了時に解放することを検証します。</summary>
    /// <param name="svg">PNG の代わりに SVG 描画を検証するかどうか。</param>
    /// <param name="entryPoint">呼び出す直接描画 API の名前。</param>
    [Theory(DisplayName = "PNG・SVG の各直接描画 API でネイティブ書体を共有し、描画終了時に解放する")]
    [InlineData(false, "pages")]
    [InlineData(true, "pages")]
    [InlineData(false, "page")]
    [InlineData(true, "page")]
    [InlineData(false, "canvas")]
    [InlineData(true, "canvas")]
    public void Direct_rendering_shares_and_releases_faces(bool svg, string entryPoint)
    {
        Assert.Null(ConversionFontResources.Current);
        var created = 0d;
        ConversionMetrics.Observer = (name, value) =>
        {
            if (name == "typefaceCreated")
            {
                created += value;
            }
        };
        var manager = new OutputFixture.FixedManager();
        var outputs = new List<MemoryStream>();
        var faces = new List<SKTypeface>();
        try
        {
            Stream Output(int page)
            {
                var owner = ConversionFontResources.Current;
                Assert.NotNull(owner);
                faces.Add(owner.GetTypeface(manager.Resolve(new("Noto Sans JP"))));
                var output = new MemoryStream();
                outputs.Add(output);
                return output;
            }

            var commands = new DrawCommand[]
            {
                new DrawTextCommand(1, new ReportRect(0, 0, 50, 20), "ABC", CellStyle.Default with { Font = new("Noto Sans JP", 10) }),
                new DrawTextCommand(2, new ReportRect(0, 0, 50, 20), "ABC", CellStyle.Default with { Font = new("Noto Sans JP", 10) }),
            };
            if (entryPoint == "pages")
            {
                if (svg)
                {
                    new SvgRenderer(manager).Render(commands, new PageSettings(50, 20), Output);
                }
                else
                {
                    new PngRenderer(manager).Render(commands, new PageSettings(50, 20), Output, 72);
                }

                Assert.Equal(2, faces.Count);
                Assert.Same(faces[0], faces[1]);
            }
            else
            {
                using var output = new MemoryStream();
                IEnumerable<DrawCommand> Enumerate()
                {
                    Output(1).Dispose();
                    foreach (var command in commands)
                    {
                        yield return command;
                    }
                }

                if (svg)
                {
                    var renderer = new SvgRenderer(manager);
                    if (entryPoint == "page")
                    {
                        renderer.RenderPage(Enumerate(), new PageSettings(50, 20), output);
                    }
                    else
                    {
                        renderer.RenderCanvas(Enumerate(), 50, 20, output);
                    }
                }
                else
                {
                    var renderer = new PngRenderer(manager);
                    if (entryPoint == "page")
                    {
                        renderer.RenderPage(Enumerate(), new PageSettings(50, 20), output, 72);
                    }
                    else
                    {
                        renderer.RenderCanvas(Enumerate(), 50, 20, output, 72);
                    }
                }

                Assert.True(output.CanWrite);
            }

            Assert.Equal(1, created);
            Assert.All(faces, face => Assert.Equal(IntPtr.Zero, face.Handle));
            Assert.Null(ConversionFontResources.Current);
        }
        finally
        {
            ConversionMetrics.Observer = null;
            foreach (var output in outputs)
            {
                output.Dispose();
            }
        }
    }

    /// <summary>描画が失敗しても外側の変換用フォント所有者を復元し、その書体を引き続き使用できることを検証します。</summary>
    /// <param name="svg">PNG の代わりに SVG 描画を検証するかどうか。</param>
    [Theory(DisplayName = "描画が失敗しても外側の変換用フォント所有者を復元し、その書体を引き続き使用できる")]
    [InlineData(false)]
    [InlineData(true)]
    public void Rendering_failure_preserves_outer_font_owner(bool svg)
    {
        using var resources = new ConversionFontResources();
        var face = resources.GetTypeface(OutputFixture.Face);
        IEnumerable<DrawCommand> Commands()
        {
            Assert.Same(resources, ConversionFontResources.Current);
            yield return new FillRectangleCommand(1, new ReportRect(0, 0, 10, 10), new ReportColor(0, 0, 255));
            throw new InvalidOperationException("Drawing failure");
        }

        using var output = new MemoryStream();
        Assert.Throws<InvalidOperationException>(() =>
        {
            if (svg)
            {
                new SvgRenderer().RenderPage(Commands(), new PageSettings(20, 20), output);
            }
            else
            {
                new PngRenderer().RenderPage(Commands(), new PageSettings(20, 20), output, 72);
            }
        });

        Assert.Same(resources, ConversionFontResources.Current);
        Assert.NotEqual(IntPtr.Zero, face.Handle);
        Assert.True(output.CanWrite);
    }

    /// <summary>描画命令の列挙中に例外が発生した場合、描画処理自身が所有するネイティブ書体を解放することを検証します。</summary>
    /// <param name="svg">PNG の代わりに SVG 描画を検証するかどうか。</param>
    [Theory(DisplayName = "描画命令の列挙中に例外が発生した場合、描画処理自身が所有するネイティブ書体を解放する")]
    [InlineData(false)]
    [InlineData(true)]
    public void Rendering_failure_releases_owned_fonts(bool svg)
    {
        SKTypeface? face = null;
        IEnumerable<DrawCommand> Commands()
        {
            var owner = ConversionFontResources.Current;
            Assert.NotNull(owner);
            face = owner.GetTypeface(OutputFixture.Face);
            yield return new FillRectangleCommand(1, new ReportRect(0, 0, 10, 10), new ReportColor(0, 0, 255));
            throw new InvalidOperationException("Drawing failure");
        }

        using var output = new MemoryStream();
        Assert.Throws<InvalidOperationException>(() =>
        {
            if (svg)
            {
                new SvgRenderer().RenderCanvas(Commands(), 20, 20, output);
            }
            else
            {
                new PngRenderer().RenderCanvas(Commands(), 20, 20, output, 72);
            }
        });

        Assert.NotNull(face);
        Assert.Equal(IntPtr.Zero, face.Handle);
        Assert.Null(ConversionFontResources.Current);
        Assert.True(output.CanWrite);
    }
}
