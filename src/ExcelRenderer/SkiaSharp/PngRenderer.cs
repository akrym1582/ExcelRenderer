using ExcelRenderer.Drawing;
using ExcelRenderer.Model;
using SkiaSharp;

namespace ExcelRenderer.SkiaSharp;

/// <summary>描画コマンドをページごとの PNG 画像として出力します。</summary>
public sealed class PngRenderer
{
    /// <summary>PNG画像の標準解像度を取得します。</summary>
    public const double DefaultDpi = 96;

    /// <summary>すべてのページを、ページ番号に対応する出力先へPNG画像として出力します。</summary>
    /// <param name="commands">出力対象の描画コマンドです。</param>
    /// <param name="pageSettings">ページの寸法と余白の設定です。</param>
    /// <param name="outputFactory">ページ番号から出力ストリームを生成する関数です。</param>
    /// <param name="dpi">出力画像の解像度です。</param>
    public void Render(
        IReadOnlyList<DrawCommand> commands,
        PageSettings pageSettings,
        Func<int, Stream> outputFactory,
        double dpi = DefaultDpi)
    {
        if (commands is null)
        {
            throw new ArgumentNullException(nameof(commands));
        }

        if (pageSettings is null)
        {
            throw new ArgumentNullException(nameof(pageSettings));
        }

        if (outputFactory is null)
        {
            throw new ArgumentNullException(nameof(outputFactory));
        }

        ValidateDpi(dpi);

        var pages = commands.GroupBy(command => command.PageNumber).OrderBy(page => page.Key).ToArray();
        if (pages.Length == 0)
        {
            using var output = outputFactory(1) ?? throw new InvalidOperationException("PNG の出力先を取得できません。");
            RenderPage([], pageSettings, output, dpi);
            return;
        }

        foreach (var page in pages)
        {
            using var output = outputFactory(page.Key) ?? throw new InvalidOperationException("PNG の出力先を取得できません。");
            RenderPage(page, pageSettings, output, dpi);
        }
    }

    /// <summary>指定した1ページ分の描画コマンドをPNG画像として出力します。</summary>
    /// <param name="commands">出力対象の描画コマンドです。</param>
    /// <param name="pageSettings">ページの寸法と余白の設定です。</param>
    /// <param name="output">PNG画像を書き込むストリームです。</param>
    /// <param name="dpi">出力画像の解像度です。</param>
    public void RenderPage(
        IEnumerable<DrawCommand> commands,
        PageSettings pageSettings,
        Stream output,
        double dpi = DefaultDpi)
    {
        if (commands is null)
        {
            throw new ArgumentNullException(nameof(commands));
        }

        if (pageSettings is null)
        {
            throw new ArgumentNullException(nameof(pageSettings));
        }

        if (output is null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        ValidateDpi(dpi);

        var scale = (float)(dpi / 72d);
        var width = Math.Max(1, (int)Math.Ceiling(pageSettings.Width * scale));
        var height = Math.Max(1, (int)Math.Ceiling(pageSettings.Height * scale));
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        canvas.Scale(scale);

        var drawingContext = new SkiaDrawingContext(textAsPaths: false);
        foreach (var command in commands)
        {
            drawingContext.Execute(canvas, command);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        data.SaveTo(output);
    }

    private static void ValidateDpi(double dpi)
    {
        if (double.IsNaN(dpi) || double.IsInfinity(dpi) || dpi <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dpi), "DPI は 0 より大きい有限値で指定してください。");
        }
    }
}
