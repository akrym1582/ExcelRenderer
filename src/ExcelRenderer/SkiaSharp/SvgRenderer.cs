using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using ExcelRenderer.Drawing;
using ExcelRenderer.Fonts;
using ExcelRenderer.Model;
using SkiaSharp;

namespace ExcelRenderer.SkiaSharp;

/// <summary>描画コマンドを、文字をパス化したページごとの自己完結 SVG として出力します。</summary>
public sealed class SvgRenderer
{
    private readonly IFontManager? _fontManager;

    /// <summary>Initializes a new instance of the <see cref="SvgRenderer"/> class. 既定の内蔵フォントポリシーを使用する SVG レンダラーを初期化します。</summary>
    public SvgRenderer()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="SvgRenderer"/> class. 指定したフォントマネージャーを使用する SVG レンダラーを初期化します。</summary>
    /// <param name="fontManager">文字の描画に使用するフォントを解決するマネージャーです。</param>
    public SvgRenderer(IFontManager fontManager) => _fontManager = fontManager ?? throw new ArgumentNullException(nameof(fontManager));

    /// <summary>すべてのページを、ページ番号に対応する出力先へ SVG として出力します。</summary>
    /// <param name="commands">出力対象の描画コマンドです。</param>
    /// <param name="pageSettings">ページの寸法と余白の設定です。</param>
    /// <param name="outputFactory">ページ番号から出力ストリームを生成する関数です。</param>
    public void Render(
        IReadOnlyList<DrawCommand> commands,
        PageSettings pageSettings,
        Func<int, Stream> outputFactory)
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

        var pages = commands.GroupBy(command => command.PageNumber).OrderBy(page => page.Key).ToArray();
        if (pages.Length == 0)
        {
            using var output = outputFactory(1) ?? throw new InvalidOperationException("SVG の出力先を取得できません。");
            RenderPage([], pageSettings, output);
            return;
        }

        foreach (var page in pages)
        {
            using var output = outputFactory(page.Key) ?? throw new InvalidOperationException("SVG の出力先を取得できません。");
            RenderPage(page, pageSettings, output);
        }
    }

    /// <summary>指定した1ページ分の描画コマンドを SVG として出力します。出力ストリームは閉じません。</summary>
    /// <param name="commands">出力対象の描画コマンドです。</param>
    /// <param name="pageSettings">ページの寸法と余白の設定です。</param>
    /// <param name="output">SVG を書き込むストリームです。</param>
    public void RenderPage(
        IEnumerable<DrawCommand> commands,
        PageSettings pageSettings,
        Stream output)
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

        if (!output.CanWrite)
        {
            throw new ArgumentException("出力ストリームは書き込み可能である必要があります。", nameof(output));
        }

        ValidateDimension(pageSettings.Width, nameof(pageSettings.Width));
        ValidateDimension(pageSettings.Height, nameof(pageSettings.Height));

        using var buffer = new MemoryStream();
        using (var canvas = SKSvgCanvas.Create(
            SKRect.Create((float)pageSettings.Width, (float)pageSettings.Height),
            buffer))
        {
            using var background = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill };
            canvas.DrawRect(0, 0, (float)pageSettings.Width, (float)pageSettings.Height, background);
            var drawingContext = new SkiaDrawingContext(textAsPaths: true, _fontManager);
            foreach (var command in commands)
            {
                drawingContext.Execute(canvas, command);
            }
        }

        buffer.Position = 0;
        var readerSettings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
        };
        XDocument document;
        using (var reader = XmlReader.Create(buffer, readerSettings))
        {
            document = XDocument.Load(reader, LoadOptions.None);
        }

        var root = document.Root ?? throw new InvalidDataException("SVG のルート要素がありません。");
        var width = Format(pageSettings.Width);
        var height = Format(pageSettings.Height);
        root.SetAttributeValue("width", width + "pt");
        root.SetAttributeValue("height", height + "pt");
        root.SetAttributeValue("viewBox", $"0 0 {width} {height}");

        var writerSettings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            CloseOutput = false,
            Indent = false,
        };
        using var writer = XmlWriter.Create(output, writerSettings);
        document.Save(writer);
    }

    private static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static void ValidateDimension(double value, string parameterName)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0 || value > float.MaxValue)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "ページ寸法は正の有限値で指定してください。");
        }
    }
}
