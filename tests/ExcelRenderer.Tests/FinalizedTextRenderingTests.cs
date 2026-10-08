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

/// <summary>確定済み文字のサイズ・書体・配置と、PDF および Skia への反映を検証します。</summary>
public sealed class FinalizedTextRenderingTests
{
    /// <summary>出力座標の検証に使う幅 120・高さ 80 ポイントのページ設定を保持します。</summary>
    private static readonly PageSettings Page = new(120, 80);

    /// <summary>フォント管理を省略した Skia 描画で、太字・斜体・縮小・パス出力の各条件でも計測時と同じシステム書体を使うことを検証します。</summary>
    /// <param name="bold">要求するシステム書体を太字にするかどうか。</param>
    /// <param name="italic">要求するシステム書体を斜体にするかどうか。</param>
    /// <param name="shrink">計測した文字を縮小して中央に配置するかどうか。</param>
    /// <param name="asPaths">選択した書体を字形パスとして描画するかどうか。</param>
    [Theory(DisplayName = "フォント管理を省略した Skia 描画で、太字・斜体・縮小・パス出力の各条件でも計測時と同じシステム書体を使う")]
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

    /// <summary>フォント管理を省略した PNG 出力で、設定済みの汎用フォントを使って従来形式の文字を描画できることを検証します。</summary>
    [Fact(DisplayName = "フォント管理を省略した PNG 出力で、設定済みの汎用フォントを使って従来形式の文字を描画できる")]
    public void Png_without_font_manager_renders_legacy_text()
    {
        Assert.True(RenderInk(new DrawTextCommand(
            1,
            new(10, 20, 100, 40),
            "Legacy text",
            CellStyle.Default with { Font = new("sans-serif", 18) })) > 0);
    }

    /// <summary>通常文字と空文字のレイアウト結果に、計測で確定した実効フォントサイズが記録されることを検証します。</summary>
    [Fact(DisplayName = "通常文字と空文字のレイアウト結果に、計測で確定した実効フォントサイズが記録される")]
    public void Layout_records_effective_font_size_for_text_and_empty_text()
    {
        var measurer = new PdfSharpTextMeasurer();
        var style = new FontStyle("Noto Sans JP", 12);

        Assert.Equal(12, measurer.Layout("ABC", style, 100, false).EffectiveFontSize);
        Assert.Equal(12, measurer.Layout(string.Empty, style, 100, false).EffectiveFontSize);
    }

    /// <summary>Skia 描画で、未指定の実効サイズにはスタイルのサイズを使い、明示したゼロサイズでは文字を描画しないことを検証します。</summary>
    [Fact(DisplayName = "Skia 描画で、未指定の実効サイズにはスタイルのサイズを使い、明示したゼロサイズでは文字を描画しない")]
    public void Skia_distinguishes_legacy_unspecified_size_from_explicit_zero()
    {
        var line = new TextLayoutLine("ABC", 30, 14, 11, [], false);
        var legacy = new TextLayoutResult(new(30, 14), [line]);
        var zero = legacy with { EffectiveFontSize = 0 };

        Assert.True(RenderInk(CreateCommand(legacy)) > 0);
        Assert.Equal(0, RenderInk(CreateCommand(zero)));
    }

    /// <summary>人工的に設定した文字ランの X 座標と行相対のベースラインが、Skia の描画位置にそのまま反映されることを検証します。</summary>
    [Fact(DisplayName = "人工的に設定した文字ランの X 座標と行相対のベースラインが、Skia の描画位置にそのまま反映される")]
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

    /// <summary>確定済みの文字ランがある場合、描画命令のスタイルに指定した未使用の主書体を Skia が解決しないことを検証します。</summary>
    [Fact(DisplayName = "確定済みの文字ランがある場合、描画命令のスタイルに指定した未使用の主書体を Skia が解決しない")]
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

    /// <summary>確定済み文字の描画中に例外が発生しても、回転とクリップを適用する前の Skia キャンバス状態に戻ることを検証します。</summary>
    /// <param name="rotation">文字描画の外側で適用する回転角度（度）。</param>
    [Theory(DisplayName = "確定済み文字の描画中に例外が発生しても、回転とクリップを適用する前の Skia キャンバス状態に戻る")]
    [InlineData(0)]
    [InlineData(30)]
    public void Skia_restores_finalized_text_state_after_drawing_exception(int rotation)
    {
        using var bitmap = new SKBitmap(80, 60);
        using var canvas = new SKCanvas(bitmap);
        var initialSaveCount = canvas.SaveCount;
        using var fixedFace = OutputFixture.Typeface();
        using var fixedFont = new SKFont(fixedFace, 10);
        var spaceGlyph = fixedFont.GetGlyphs(" ")[0];
        using var emptyOutline = fixedFont.GetGlyphPath(spaceGlyph);
        Assert.True(emptyOutline is null || emptyOutline.IsEmpty);
        var invalidRun = new TextRun("X", MemoryFont())
        {
            GlyphId = spaceGlyph,
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

    /// <summary>メモリ上だけに存在するフォントを PDF と Skia が使用でき、通常の PDF 文字はテキストとして出力されることを検証します。</summary>
    [Fact(DisplayName = "メモリ上だけに存在するフォントを PDF と Skia が使用でき、通常の PDF 文字はテキストとして出力される")]
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

    /// <summary>確定済み PDF 文字の下線を手動で描く際に、フォント側の自動下線が無効になっていることを検証します。</summary>
    [Fact(DisplayName = "確定済み PDF 文字の下線を手動で描く際に、フォント側の自動下線が無効になっている")]
    public void Finalized_pdf_font_does_not_enable_automatic_underline()
    {
        var font = PdfSharpTextMeasurer.CreateFont(
            new FontStyle("Noto Sans JP", 12, Underline: true),
            includeUnderline: false);

        Assert.False(font.Style.HasFlag(XFontStyleEx.Underline));
    }

    /// <summary>ファミリ名とファイルパスが同じ書体でも、識別子とフォントデータの違いで PDF 用のキーを区別することを検証します。</summary>
    [Fact(DisplayName = "ファミリ名とファイルパスが同じ書体でも、識別子とフォントデータの違いで PDF 用のキーを区別する")]
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

    /// <summary>並行する PDF 書体登録が一度だけ実行され、登録後に元のバイト配列を変更しても保存済みデータが変わらないことを検証します。</summary>
    [Fact(DisplayName = "並行する PDF 書体登録が一度だけ実行され、登録後に元のバイト配列を変更しても保存済みデータが変わらない")]
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

    /// <summary>フォント管理と文字計測を組み合わせた繰り返しの折り返し計測で、同じ PDF 書体登録を再利用することを検証します。</summary>
    [Fact(DisplayName = "フォント管理と文字計測を組み合わせた繰り返しの折り返し計測で、同じ PDF 書体登録を再利用する")]
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

    /// <summary>ファイル由来の PDF 書体を繰り返し登録しても、登録用データの読み込みとハッシュ計算が一度だけ行われることを検証します。</summary>
    [Fact(DisplayName = "ファイル由来の PDF 書体を繰り返し登録しても、登録用データの読み込みとハッシュ計算が一度だけ行われる")]
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

    /// <summary>負数・NaN・無限大の実効フォントサイズを指定すると、公開レイアウト状態の生成時に拒否されることを検証します。</summary>
    /// <param name="size">検証する実効フォントサイズ（ポイント）。</param>
    [Theory(DisplayName = "負数・NaN・無限大の実効フォントサイズを指定すると、公開レイアウト状態の生成時に拒否される")]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Effective_size_rejects_invalid_values(double size)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TextLayoutResult(new(0, 0), []) { EffectiveFontSize = size });
    }

    /// <summary>指定した確定済み文字レイアウトを、固定の文字領域を持つ描画命令に設定します。</summary>
    /// <param name="layout">検証する画像レイアウト、または確定済み文字レイアウト。</param>
    private static DrawTextCommand CreateCommand(TextLayoutResult layout) => new(
        1,
        new(10, 20, 100, 50),
        string.Concat(layout.Lines.Select(line => line.Text)),
        CellStyle.Default with { Font = new FontStyle("Noto Sans JP", 12) })
    {
        TextLayout = layout,
    };

    /// <summary>同梱の Noto Sans JP をバイト配列として持つ、ファイルパスに依存しない書体を作成します。</summary>
    private static ResolvedFont MemoryFont()
    {
        var data = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "NotoSansJP-Regular.ttf"));
        return new("Same Family", 400, false, string.Empty)
        {
            FaceId = "memory-noto-regular",
            FontData = data,
        };
    }

    /// <summary>二つのビットマップの寸法と各画素の色成分を、指定した許容差で比較します。</summary>
    /// <param name="expected">判定または数値比較の期待値。</param>
    /// <param name="actual">比較対象の実際値。</param>
    /// <param name="tolerance">色成分の比較で許容する差。</param>
    private static void AssertBitmapsEqual(SKBitmap expected, SKBitmap actual, int tolerance = 0)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        for (var y = 0; y < expected.Height; y++)
        {
            for (var x = 0; x < expected.Width; x++)
            {
                var expectedPixel = expected.GetPixel(x, y);
                var actualPixel = actual.GetPixel(x, y);
                Assert.True(Math.Abs(expectedPixel.Red - actualPixel.Red) <= tolerance);
                Assert.True(Math.Abs(expectedPixel.Green - actualPixel.Green) <= tolerance);
                Assert.True(Math.Abs(expectedPixel.Blue - actualPixel.Blue) <= tolerance);
                Assert.True(Math.Abs(expectedPixel.Alpha - actualPixel.Alpha) <= tolerance);
            }
        }
    }

    /// <summary>文字描画命令を PNG としてメモリストリームへ描画し、検証用の出力を返します。</summary>
    /// <param name="command">実行するコマンド、または描画対象の文字命令。</param>
    private static MemoryStream Render(DrawTextCommand command)
    {
        var output = new MemoryStream();
        new PngRenderer().RenderPage([command], Page, output, 72);
        output.Position = 0;
        return output;
    }

    /// <summary>文字描画命令を PNG に描画して、文字が描かれた画素数を返します。</summary>
    /// <param name="command">実行するコマンド、または描画対象の文字命令。</param>
    private static int RenderInk(DrawTextCommand command)
    {
        using var output = Render(command);
        using var bitmap = SKBitmap.Decode(output.ToArray());
        return Enumerable.Range(0, bitmap.Width).Sum(x =>
            Enumerable.Range(0, bitmap.Height).Count(y => bitmap.GetPixel(x, y) != SKColors.White));
    }

    /// <summary>指定したビットマップ領域に文字の描画画素が存在するかを調べます。</summary>
    /// <param name="bitmap">画素を検証または保存するビットマップ。</param>
    /// <param name="left">検査領域の左端画素位置。</param>
    /// <param name="right">検査領域の右端画素位置。</param>
    /// <param name="top">検査領域の上端画素位置。</param>
    /// <param name="bottom">検査領域の下端画素位置。</param>
    private static bool HasInk(SKBitmap bitmap, int left, int right, int top, int bottom) =>
        Enumerable.Range(left, right - left).Any(x =>
            Enumerable.Range(top, bottom - top).Any(y => bitmap.GetPixel(x, y) != SKColors.White));
}
