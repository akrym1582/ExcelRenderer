using ExcelRenderer.Drawing;
using ExcelRenderer.Fonts;
using ExcelRenderer.Model;
using SkiaSharp;

namespace ExcelRenderer.SkiaSharp;

/// <summary>描画コマンドをページごとの PNG 画像として出力します。</summary>
public sealed class PngRenderer
{
    /// <summary>PNG画像の標準解像度を取得します。</summary>
    public const double DefaultDpi = 96;

    /// <summary>Default maximum number of pixels for one bitmap.</summary>
    public const long DefaultMaxPixels = 100_000_000;

    private readonly IFontManager? _fontManager;

    /// <summary>Initializes a new instance of the <see cref="PngRenderer"/> class. Skia のシステムフォント選択を使用する PNG レンダラーを初期化します。</summary>
    public PngRenderer()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PngRenderer"/> class. 指定したフォントマネージャーを使用する PNG レンダラーを初期化します。</summary>
    /// <param name="fontManager">文字の描画に使用するフォントを解決するマネージャーです。</param>
    public PngRenderer(IFontManager fontManager) => _fontManager = fontManager ?? throw new ArgumentNullException(nameof(fontManager));

    /// <summary>ポイント寸法を安全な PNG ピクセル寸法へ変換します。</summary>
    /// <param name="widthPoints">キャンバスの幅をポイント単位で指定します。</param>
    /// <param name="heightPoints">キャンバスの高さをポイント単位で指定します。</param>
    /// <param name="dpi">ピクセル寸法への変換に使用する解像度です。</param>
    /// <param name="maxPixels">許容する最大ピクセル数です。</param>
    /// <returns>変換済みの幅と高さをピクセル単位で返します。</returns>
    public static (int Width, int Height) GetPixelDimensions(double widthPoints, double heightPoints, double dpi, long maxPixels)
    {
        if (double.IsNaN(widthPoints) || double.IsInfinity(widthPoints) || widthPoints <= 0 ||
            double.IsNaN(heightPoints) || double.IsInfinity(heightPoints) || heightPoints <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(widthPoints), "Canvas dimensions must be positive finite values.");
        }

        ValidateDpi(dpi);
        if (maxPixels <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPixels));
        }

        var scale = dpi / 72d;
        var widthValue = Math.Ceiling(widthPoints * scale);
        var heightValue = Math.Ceiling(heightPoints * scale);
        if (widthValue > int.MaxValue || heightValue > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(widthPoints), "PNG canvas dimensions exceed Skia bitmap limits.");
        }

        var width = Math.Max(1, (int)widthValue);
        var height = Math.Max(1, (int)heightValue);
        long pixels;
        try
        {
            pixels = checked((long)width * height);
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(nameof(widthPoints), "PNG canvas pixel count overflows.");
        }

        if (pixels > maxPixels)
        {
            throw new ArgumentOutOfRangeException(nameof(widthPoints), $"PNG canvas requires {width}x{height} ({pixels} pixels), exceeding the {maxPixels} pixel limit. Lower DPI or reduce the worksheet content.");
        }

        return (width, height);
    }

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

        var drawingContext = new SkiaDrawingContext(textAsPaths: false, _fontManager);
        foreach (var command in commands)
        {
            drawingContext.Execute(canvas, command);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        data.SaveTo(output);
    }

    /// <summary>指定されたポイント寸法の単一キャンバスを PNG として出力します。</summary>
    /// <param name="commands">出力対象の描画コマンドです。</param>
    /// <param name="widthPoints">キャンバスの幅をポイント単位で指定します。</param>
    /// <param name="heightPoints">キャンバスの高さをポイント単位で指定します。</param>
    /// <param name="output">PNG を書き込むストリームです。</param>
    /// <param name="dpi">出力画像の解像度です。</param>
    /// <param name="maxPixels">許容する最大ピクセル数です。</param>
    public void RenderCanvas(
        IEnumerable<DrawCommand> commands,
        double widthPoints,
        double heightPoints,
        Stream output,
        double dpi = DefaultDpi,
        long maxPixels = DefaultMaxPixels)
    {
        if (commands is null)
        {
            throw new ArgumentNullException(nameof(commands));
        }

        if (output is null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        ValidateDpi(dpi);
        if (maxPixels <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPixels), "Maximum PNG pixels must be positive.");
        }

        var (width, height) = GetPixelDimensions(widthPoints, heightPoints, dpi, maxPixels);
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        canvas.Scale((float)(dpi / 72d));
        var drawingContext = new SkiaDrawingContext(textAsPaths: false, _fontManager);
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
