using System.Text;
using System.Xml.Linq;
using ExcelRenderer.Drawing;
using ExcelRenderer.SkiaSharp;
using SkiaSharp;
using Svg.Skia;
using Xunit;

namespace ExcelRenderer.Tests;

/// <summary>文字命令を PNG・SVG に描画し、SVG の寸法を確認して比較用ビットマップを構築します。</summary>
internal sealed class SerializedSvgProbe : IDisposable
{
    /// <summary>描画命令から出力した SVG のバイト列を保持します。</summary>
    internal byte[] Svg { get; }

    /// <summary>直接 PNG 出力を復号した、比較基準のビットマップを保持します。</summary>
    internal SKBitmap Png { get; }

    /// <summary>保存済み SVG を再読込して 120 × 80 画素に描画した結果を保持します。</summary>
    internal SKBitmap Raster { get; }

    /// <summary>文字命令を PNG・SVG に描画し、SVG の寸法を確認して比較用ビットマップを構築します。</summary>
    /// <param name="command">実行するコマンド、または描画対象の文字命令。</param>
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

    /// <summary>指定画素の赤成分が 250 未満かどうかを調べ、白背景から文字画素を区別します。</summary>
    /// <param name="bitmap">画素を検証または保存するビットマップ。</param>
    /// <param name="x">描画位置または比較対象の X 座標。</param>
    /// <param name="y">描画位置または比較対象の Y 座標。</param>
    internal static bool Ink(SKBitmap bitmap, int x, int y) =>
        bitmap.GetPixel(x, y).Red < 250; // Same opaque white background and monochrome text in both outputs.

    /// <summary>PNG と SVG の再描画を双方向で比較し、追加検証の失敗時にも SVG・画像・差分を保存します。</summary>
    /// <param name="name">比較失敗時の成果物を保存するサブディレクトリ名。</param>
    /// <param name="extra">画素比較後に実行する追加の検証処理。</param>
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

    /// <summary>指定領域にある文字画素の外接矩形を求め、文字画素がない場合は検証を失敗させます。</summary>
    /// <param name="bitmap">画素を検証または保存するビットマップ。</param>
    /// <param name="region">文字画素を検査する矩形領域。</param>
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

    /// <summary>描画画素それぞれについて、比較対象の周囲 1 画素以内に対応する描画画素があることを検証します。</summary>
    /// <param name="source">比較元の画像、または読み取り元のストリーム。</param>
    /// <param name="target">検証するリンク先文字列、または比較先の画像。</param>
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

    /// <summary>ビットマップを PNG として指定したパスに保存します。</summary>
    /// <param name="bitmap">画素を検証または保存するビットマップ。</param>
    /// <param name="path">読み書きに使用するファイルパス。</param>
    private static void Save(SKBitmap bitmap, string path)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, png.ToArray());
    }

    /// <summary>比較用に保持している PNG と SVG 再描画のビットマップを解放します。</summary>
    public void Dispose() { Png.Dispose(); Raster.Dispose(); }
}
