using ExcelRenderer.TestFixtures;
using ExcelRenderer.SkiaSharp;
using ExcelRenderer.Abstractions;
using ExcelRenderer.Drawing;
using ExcelRenderer.Fonts;
using ExcelRenderer.Model;
using ExcelRenderer.Pdf;
using SkiaSharp;
using Xunit;

namespace ExcelRenderer.Tests;

/// <summary>文字描画が失敗した場合も、Skia と PDF の呼び出し元の変換・クリップ状態を復元することを検証します。</summary>
public sealed class DrawingStateRegressionTests
{
    /// <summary>従来形式の Skia 文字描画が失敗しても、回転と呼び出し元の保存状態にかかわらずキャンバスを復元することを検証します。</summary>
    /// <param name="rotation">文字に適用する回転角度（度）。</param>
    /// <param name="callerState">呼び出し元で変換・クリップ状態を設定するかどうか。</param>
    [Theory(DisplayName = "従来形式の Skia 文字描画が失敗しても、回転と呼び出し元の保存状態にかかわらずキャンバスを復元する")]
    [InlineData(0, false)]
    [InlineData(30, false)]
    [InlineData(0, true)]
    [InlineData(30, true)]
    public void Skia_legacy_restores_canvas_after_drawing_failure(int rotation, bool callerState) => Skia(false, rotation, callerState);

    /// <summary>確定済み Skia 文字描画が失敗しても、回転と呼び出し元の保存状態にかかわらずキャンバスを復元することを検証します。</summary>
    /// <param name="rotation">文字に適用する回転角度（度）。</param>
    /// <param name="callerState">呼び出し元で変換・クリップ状態を設定するかどうか。</param>
    [Theory(DisplayName = "確定済み Skia 文字描画が失敗しても、回転と呼び出し元の保存状態にかかわらずキャンバスを復元する")]
    [InlineData(0, false)]
    [InlineData(30, false)]
    [InlineData(0, true)]
    [InlineData(30, true)]
    public void Skia_finalized_restores_canvas_after_drawing_failure(int rotation, bool callerState) => Skia(true, rotation, callerState);

    /// <summary>確定済み PDF 文字描画が失敗しても、回転と呼び出し元の保存状態にかかわらずグラフィックスを復元することを検証します。</summary>
    /// <param name="rotation">文字に適用する回転角度（度）。</param>
    /// <param name="callerState">呼び出し元で変換・クリップ状態を設定するかどうか。</param>
    [Theory(DisplayName = "確定済み PDF 文字描画が失敗しても、回転と呼び出し元の保存状態にかかわらずグラフィックスを復元する")]
    [InlineData(0, false)]
    [InlineData(30, false)]
    [InlineData(0, true)]
    [InlineData(30, true)]
    public void Pdf_finalized_restores_graphics_after_drawing_failure(int rotation, bool callerState) => Pdf(true, rotation, callerState);

    /// <summary>従来形式の PDF 文字描画が失敗しても、回転と呼び出し元の保存状態にかかわらずグラフィックスを復元することを検証します。</summary>
    /// <param name="rotation">文字に適用する回転角度（度）。</param>
    /// <param name="callerState">呼び出し元で変換・クリップ状態を設定するかどうか。</param>
    [Theory(DisplayName = "従来形式の PDF 文字描画が失敗しても、回転と呼び出し元の保存状態にかかわらずグラフィックスを復元する")]
    [InlineData(0, false)]
    [InlineData(30, false)]
    [InlineData(0, true)]
    [InlineData(30, true)]
    public void Pdf_legacy_restores_graphics_after_drawing_failure(int rotation, bool callerState) => Pdf(false, rotation, callerState);

    /// <summary>従来形式の通常 PDF 文字を描画した後も、呼び出し元の変換とクリップが保持されることを検証します。</summary>
    /// <param name="rotation">文字に適用する回転角度（度）。</param>
    [Theory(DisplayName = "従来形式の通常 PDF 文字を描画した後も、呼び出し元の変換とクリップが保持される")]
    [InlineData(0)]
    [InlineData(30)]
    public void Pdf_legacy_ordinary_text_preserves_caller_transform_and_clip(int rotation)
    {
        using var output = new MemoryStream();
        using (var document = SKDocument.CreatePdf(output))
        {
            var canvas = document.BeginPage(120, 80);
            canvas.Translate(4, 3);
            canvas.ClipRect(new(0, 0, 116, 77));
            var before = canvas.TotalMatrix;
            var clipBefore = canvas.DeviceClipBounds;
            var savesBefore = canvas.SaveCount;
            new SkiaTextPainter(false, new OutputFixture.FixedManager()).Paint(canvas, Command(false, rotation));
            Assert.Equal(before, canvas.TotalMatrix);
            Assert.Equal(clipBefore, canvas.DeviceClipBounds);
            Assert.Equal(savesBefore, canvas.SaveCount);
            using var red = new SKPaint { Color = SKColors.Red };
            canvas.DrawRect(110, 72, 4, 4, red);
            document.EndPage(); document.Close();
        }
        var probe = PdfContentProbe.Read(output.ToArray());
        var sentinel = Assert.Single(probe.Paints);
        PdfOutputRegressionTests.Near(114, sentinel.Points.Min(p => p.X));
        PdfOutputRegressionTests.Near(75, sentinel.Points.Min(p => p.Y));
        PdfOutputRegressionTests.Near(118, sentinel.Points.Max(p => p.X));
        PdfOutputRegressionTests.Near(79, sentinel.Points.Max(p => p.Y));
    }

    /// <summary>例外時の状態復元を検証するため、回転と確定済みレイアウトの有無を指定した文字描画命令を作成します。</summary>
    /// <param name="finalized">確定済み文字レイアウトを使うかどうか。</param>
    /// <param name="rotation">文字に適用する回転角度（度）。</param>
    private static DrawTextCommand Command(bool finalized, int rotation)
    {
        using var face = OutputFixture.Typeface();
        using var font = new SKFont(face, 9);
        // A known, valid space glyph has no outline. Loading and measurement succeed; actual path drawing throws.
        var glyph = font.GetGlyphs(" ")[0];
        Assert.NotEqual((ushort)0, glyph);
        using var path = font.GetGlyphPath(glyph);
        Assert.True(path is null || path.IsEmpty);
        return new(1, new(10, 20, 100, 50), "A", CellStyle.Default with
        {
            Font = new("Noto Sans JP", 12), WrapText = true, TextRotation = rotation,
        })
        {
            TextLayout = finalized ? new TextLayoutResult(new(20, 12),
                [new("A", 20, 12, 9, [new(new("A", OutputFixture.Face) { GlyphId = glyph }, 0, 10)], false)])
                { EffectiveFontSize = 9 } : null,
        };
    }

    /// <summary>Skia 文字描画を意図的に失敗させ、描画前後の変換・クリップ・保存数を比較します。</summary>
    /// <param name="finalized">確定済み文字レイアウトを使うかどうか。</param>
    /// <param name="rotation">文字に適用する回転角度（度）。</param>
    /// <param name="callerState">呼び出し元で変換・クリップ状態を設定するかどうか。</param>
    private static void Skia(bool finalized, int rotation, bool callerState)
    {
        using var bitmap = new SKBitmap(120, 80);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        if (callerState)
        {
            canvas.Translate(4, 3);
            canvas.ClipRect(new(0, 0, 116, 77));
        }
        var saveCount = canvas.SaveCount;
        var originalMatrix = canvas.TotalMatrix;
        var originalClip = canvas.DeviceClipBounds;
        var manager = new DrawingFailureManager();
        var painter = new SkiaTextPainter(true, manager, () =>
        {
            Assert.Equal(saveCount + (rotation == 0 ? 1 : 2), canvas.SaveCount);
            Assert.NotEqual(originalClip, canvas.DeviceClipBounds);
            manager.Armed = true;
        });
        var exception = Assert.Throws<InvalidOperationException>(() => painter.Paint(canvas, Command(finalized, rotation)));
        if (!finalized)
        {
            Assert.Same(manager.Failure, exception);
            Assert.Equal("ResolveTextRuns during drawing", manager.FailedOperation);
            Assert.True(manager.SuccessfulResolutions > 0);
        }
        else { Assert.Contains("アウトライン", exception.Message); }
        Assert.Equal(saveCount, canvas.SaveCount);
        Assert.Equal(originalMatrix, canvas.TotalMatrix);
        Assert.Equal(originalClip, canvas.DeviceClipBounds);
        using var paint = new SKPaint { Color = SKColors.Red };
        canvas.DrawRect(new SKRect(110, 72, 114, 76), paint);
        Assert.Equal(SKColors.Red, bitmap.GetPixel(callerState ? 115 : 111, callerState ? 76 : 73));
        Assert.Equal(SKColors.White, bitmap.GetPixel(callerState ? 111 : 115, callerState ? 73 : 77));
        // A marker outside the caller clip must still be clipped after failure.
        if (callerState)
        {
            canvas.DrawRect(new SKRect(-4, -3, 0, 0), paint);
            Assert.Equal(SKColors.White, bitmap.GetPixel(1, 1));
        }
    }

    /// <summary>PDF 文字描画を意図的に失敗させ、描画前後のグラフィックスの状態を比較します。</summary>
    /// <param name="finalized">確定済み文字レイアウトを使うかどうか。</param>
    /// <param name="rotation">文字に適用する回転角度（度）。</param>
    /// <param name="callerState">呼び出し元で変換・クリップ状態を設定するかどうか。</param>
    private static void Pdf(bool finalized, int rotation, bool callerState)
    {
        using var output = new MemoryStream();
        using (var document = SKDocument.CreatePdf(output))
        {
            var canvas = document.BeginPage(120, 80);
            if (callerState) { canvas.Translate(4, 3); canvas.ClipRect(new(0, 0, 116, 77)); }
            var original = canvas.TotalMatrix;
            var clip = canvas.DeviceClipBounds;
            var saved = canvas.SaveCount;
            var manager = new DrawingFailureManager();
            var painter = new SkiaTextPainter(true, manager, () => manager.Armed = true);
            var error = Assert.Throws<InvalidOperationException>(() => painter.Paint(canvas, Command(finalized, rotation)));
            if (!finalized) Assert.Same(manager.Failure, error);
            else Assert.Contains("アウトライン", error.Message);
            Assert.Equal(original, canvas.TotalMatrix);
            Assert.Equal(clip, canvas.DeviceClipBounds);
            Assert.Equal(saved, canvas.SaveCount);
            using var red = new SKPaint { Color = SKColors.Red };
            canvas.DrawRect(110, 72, 4, 4, red);
            document.EndPage(); document.Close();
        }
        var probe = PdfContentProbe.Read(output.ToArray());
        var sentinel = Assert.Single(probe.Paints);
        var x = callerState ? 114 : 110; var y = callerState ? 75 : 72;
        Assert.Equal(x, sentinel.Points.Min(p => p.X), 2);
        Assert.Equal(y, sentinel.Points.Min(p => p.Y), 2);
        Assert.Equal(x + 4, sentinel.Points.Max(p => p.X), 2);
        Assert.Equal(y + 4, sentinel.Points.Max(p => p.Y), 2);
    }

    /// <summary>書体解決時に制御された例外を発生させる、描画失敗テスト用のフォント管理です。</summary>
    private sealed class DrawingFailureManager : IFontManager
    {
        /// <summary>文字ラン解決時に描画失敗の例外を送出するかどうかを保持します。</summary>
        internal bool Armed { get; set; }

        /// <summary>例外を送出した文字ラン解決操作の名前を保持します。</summary>
        internal string? FailedOperation { get; private set; }

        /// <summary>例外を送出せずに文字ランを解決した回数を保持します。</summary>
        internal int SuccessfulResolutions { get; private set; }

        /// <summary>描画失敗時に送出して同一性を検証する例外を保持します。</summary>
        internal InvalidOperationException Failure { get; } = new("Controlled drawing failure");

        /// <summary>書体要求に対して、このテストで設定した固定書体を返します。</summary>
        /// <param name="request">解決を要求するフォントファミリと書体設定（固定書体のテスト実装では選択に使用しません）。</param>
        public ResolvedFont Resolve(FontRequest request) => OutputFixture.Face;

        /// <summary>通常は字形 A を含む固定書体の文字ランを返し、失敗フラグ設定後は保存した例外を送出します。</summary>
        /// <param name="text">計測・描画または解析の対象となる文字列。</param>
        /// <param name="request">解決を要求するフォントファミリと書体設定（固定書体のテスト実装では選択に使用しません）。</param>
        public IReadOnlyList<TextRun> ResolveTextRuns(string text, FontRequest request)
        {
            if (Armed) { FailedOperation = "ResolveTextRuns during drawing"; throw Failure; }
            SuccessfulResolutions++;
            using var face = OutputFixture.Typeface(); using var font = new SKFont(face, 12);
            return [new(text, OutputFixture.Face) { GlyphId = font.GetGlyphs("A")[0] }];
        }
    }
}
