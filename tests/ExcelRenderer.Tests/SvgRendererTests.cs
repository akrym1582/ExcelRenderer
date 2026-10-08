using System.Xml.Linq;
using ExcelRenderer.Drawing;
using ExcelRenderer.Fonts;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using ExcelRenderer.SkiaSharp;
using SkiaSharp;
using Xunit;

namespace ExcelRenderer.Tests;

/// <summary>描画命令から生成する SVG の構造とストリーム契約を検証します。</summary>
public sealed class SvgRendererTests
{
    /// <summary>正確なポイント寸法、白背景、パス化文字を持つ SVG を生成することを検証します。</summary>
    [Fact(DisplayName = "正確なポイント寸法、白背景、パス化文字を持つ SVG を生成する")]
    public void RenderPage_writes_point_dimensions_and_outlined_text()
    {
        using var output = new MemoryStream();
        new SvgRenderer().RenderPage(
            [new DrawTextCommand(1, new ReportRect(5, 5, 180, 40), "請求書 A  B", CellStyle.Default)],
            new PageSettings(595.276, 841.89),
            output);

        Assert.True(output.CanWrite);
        var document = XDocument.Parse(System.Text.Encoding.UTF8.GetString(output.ToArray()));
        var root = document.Root!;
        XNamespace svg = "http://www.w3.org/2000/svg";
        Assert.Equal(svg + "svg", root.Name);
        Assert.Equal("595.276pt", root.Attribute("width")?.Value);
        Assert.Equal("841.89pt", root.Attribute("height")?.Value);
        Assert.Equal("0 0 595.276 841.89", root.Attribute("viewBox")?.Value);
        Assert.NotEmpty(root.Descendants(svg + "path"));
        Assert.Empty(root.Descendants(svg + "text"));
        Assert.Empty(root.Descendants(svg + "tspan"));
    }

    /// <summary>画像が外部 URL ではなくデータ URI として SVG 内へ格納されることを検証します。</summary>
    [Fact(DisplayName = "画像が外部 URL ではなくデータ URI として SVG 内へ格納される")]
    public void RenderPage_embeds_images()
    {
        using var bitmap = new SKBitmap(2, 2);
        bitmap.Erase(SKColors.Blue);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        using var output = new MemoryStream();

        new SvgRenderer().RenderPage(
            [new DrawImageCommand(1, new ReportRect(2, 2, 10, 10), encoded.ToArray())],
            new PageSettings(20, 20),
            output);

        var document = XDocument.Parse(System.Text.Encoding.UTF8.GetString(output.ToArray()));
        XNamespace svg = "http://www.w3.org/2000/svg";
        var imageElement = Assert.Single(document.Descendants(svg + "image"));
        Assert.Contains(imageElement.Attributes(), attribute => attribute.Value.StartsWith("data:image/png;base64,", StringComparison.Ordinal));
    }

    /// <summary>カラー絵文字を SVG に自己完結の画像として格納します。</summary>
    [Fact(DisplayName = "カラー絵文字を SVG に自己完結の画像として格納します")]
    public void RenderPage_embeds_color_emoji()
    {
        var manager = new FontManager(new FontOptions { AllowSystemFonts = false, FallbackFamilies = [] });
        using var output = new MemoryStream();
        new SvgRenderer(manager).RenderPage(
            [new DrawTextCommand(1, new ReportRect(4, 4, 80, 58), "😀", CellStyle.Default with
            { Font = new FontStyle("Noto Sans JP", 40) })],
            new PageSettings(88, 66), output);

        var document = XDocument.Parse(System.Text.Encoding.UTF8.GetString(output.ToArray()));
        XNamespace svg = "http://www.w3.org/2000/svg";
        var image = Assert.Single(document.Descendants(svg + "image"));
        Assert.Contains(image.Attributes(), attribute => attribute.Value.StartsWith("data:image/png;base64,", StringComparison.Ordinal));
    }

    /// <summary>seek 非対応の書き込み可能ストリームにも完全な SVG を出力できることを検証します。</summary>
    [Fact(DisplayName = "seek 非対応の書き込み可能ストリームにも完全な SVG を出力できる")]
    public void RenderPage_supports_non_seekable_output()
    {
        using var output = new NonSeekableWriteStream();
        new SvgRenderer().RenderPage([], new PageSettings(20, 30), output);

        Assert.Equal("0 0 20 30", XDocument.Parse(output.Text).Root?.Attribute("viewBox")?.Value);
    }

    /// <summary>複数ページ出力が factory のストリームをページごとに閉じることを検証します。</summary>
    [Fact(DisplayName = "複数ページ出力が factory のストリームをページごとに閉じる")]
    public void Render_disposes_each_factory_stream()
    {
        var streams = new List<CaptureOnDisposeStream>();
        new SvgRenderer().Render(
            [
                new FillRectangleCommand(2, new ReportRect(0, 0, 2, 2), new ReportColor(0, 0, 255)),
                new FillRectangleCommand(1, new ReportRect(0, 0, 2, 2), new ReportColor(255, 0, 0)),
            ],
            new PageSettings(10, 10),
            _ =>
            {
                var stream = new CaptureOnDisposeStream();
                streams.Add(stream);
                return stream;
            });

        Assert.Equal(2, streams.Count);
        Assert.All(streams, stream => Assert.True(stream.WasDisposed));
        Assert.All(streams, stream => Assert.NotEmpty(stream.Bytes));
    }

    /// <summary>ページ描画が失敗した場合も factory が返したストリームを閉じることを検証します。</summary>
    [Fact(DisplayName = "ページ描画が失敗した場合も factory が返したストリームを閉じる")]
    public void Render_disposes_factory_stream_when_rendering_fails()
    {
        var output = new CaptureOnDisposeStream();
        Assert.Throws<ArgumentOutOfRangeException>(() => new SvgRenderer().Render(
            [new FillRectangleCommand(1, new ReportRect(0, 0, 2, 2), new ReportColor(0, 0, 255))],
            new PageSettings(double.NaN, 10),
            _ => output));

        Assert.True(output.WasDisposed);
    }

    /// <summary>不正なページ寸法と書き込み不可ストリームを拒否することを検証します。</summary>
    [Fact(DisplayName = "不正なページ寸法と書き込み不可ストリームを拒否する")]
    public void RenderPage_validates_dimensions_and_output()
    {
        using var writable = new MemoryStream();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SvgRenderer().RenderPage([], new PageSettings(double.NaN, 10), writable));
        using var readOnly = new MemoryStream(Array.Empty<byte>(), writable: false);
        Assert.Throws<ArgumentException>(() =>
            new SvgRenderer().RenderPage([], new PageSettings(10, 10), readOnly));
    }

    /// <summary>終了時に出力データを保存し、描画処理がストリームを閉じたことを検証します。</summary>
    private sealed class CaptureOnDisposeStream : MemoryStream
    {
        /// <summary>出力ストリームの終了処理が呼び出されたかどうかを保持します。</summary>
        public bool WasDisposed { get; private set; }

        /// <summary>ストリーム終了時に保存した SVG のバイト列を保持します。</summary>
        public byte[] Bytes { get; private set; } = [];

        /// <summary>終了時の出力バイト列を保存し、基底ストリームを閉じます。</summary>
        /// <param name="disposing">マネージド資源も解放するかどうか。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Bytes = ToArray();
                WasDisposed = true;
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>シークを禁止しながらメモリに書き込み、SVG のストリーム出力契約を検証します。</summary>
    private sealed class NonSeekableWriteStream : Stream
    {
        /// <summary>シーク非対応ストリームの書き込み内容を保持する内部バッファです。</summary>
        private readonly MemoryStream _inner = new();

        /// <summary>内部バッファの内容を UTF-8 の SVG 文字列として返します。</summary>
        public string Text => System.Text.Encoding.UTF8.GetString(_inner.ToArray());

        /// <summary>読み取りをサポートしないため、false を返します。</summary>
        public override bool CanRead => false;

        /// <summary>この検証用ストリームはシークをサポートしないため、false を返します。</summary>
        public override bool CanSeek => false;

        /// <summary>出力先として使用できるよう、書き込み対応を表す true を返します。</summary>
        public override bool CanWrite => true;

        /// <summary>シーク非対応を再現するため、長さの取得で NotSupportedException を送出します。</summary>
        public override long Length => throw new NotSupportedException();

        /// <summary>シーク非対応を再現するため、現在位置の取得と変更で NotSupportedException を送出します。</summary>
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        /// <summary>内部バッファをフラッシュします。</summary>
        public override void Flush() => _inner.Flush();

        /// <summary>読み取り非対応のため NotSupportedException を送出します。</summary>
        /// <param name="buffer">読み書きに使用するバイト配列。</param>
        /// <param name="offset">バッファ内の読み書き開始位置。</param>
        /// <param name="count">バッファから読み書きする最大バイト数。</param>
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        /// <summary>シーク非対応を再現するため、読み書き位置の移動で NotSupportedException を送出します。</summary>
        /// <param name="offset">基準位置からの移動量（バイト）。</param>
        /// <param name="origin">シークの基準位置。</param>
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        /// <summary>シーク非対応を再現するため、長さの変更で NotSupportedException を送出します。</summary>
        /// <param name="value">設定するストリームの長さ（バイト）。</param>
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <summary>指定されたバッファの範囲を内部メモリストリームへ書き込みます。</summary>
        /// <param name="buffer">読み書きに使用するバイト配列。</param>
        /// <param name="offset">バッファ内の読み書き開始位置。</param>
        /// <param name="count">バッファから読み書きする最大バイト数。</param>
        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

        /// <summary>内部メモリストリームと基底ストリームを閉じます。</summary>
        /// <param name="disposing">マネージド資源も解放するかどうか。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
