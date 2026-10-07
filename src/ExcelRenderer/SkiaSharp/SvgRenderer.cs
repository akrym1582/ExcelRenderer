using System.Globalization;
using System.Text;
using System.Xml;
using ExcelRenderer.Drawing;
using ExcelRenderer.Fonts;
using ExcelRenderer.Model;
using ExcelRenderer.Rendering;
using SkiaSharp;

namespace ExcelRenderer.SkiaSharp;

/// <summary>描画コマンドを、文字をパス化したページごとの自己完結 SVG として出力します。</summary>
public sealed class SvgRenderer
{
    private readonly IFontManager? _fontManager;

    /// <summary>Initializes a new instance of the <see cref="SvgRenderer"/> class. Skia のシステムフォント選択を使用する SVG レンダラーを初期化します。</summary>
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
        => Render(commands, pageSettings, outputFactory, new RenderBufferOptions());

    /// <summary>Renders every page with the specified bounded SVG intermediate buffer.</summary>
    /// <param name="commands">The materialized compatibility commands.</param>
    /// <param name="pageSettings">The page dimensions in points.</param>
    /// <param name="outputFactory">The page output factory.</param>
    /// <param name="buffering">The intermediate buffer policy.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public void Render(IReadOnlyList<DrawCommand> commands, PageSettings pageSettings, Func<int, Stream> outputFactory, RenderBufferOptions buffering, CancellationToken cancellationToken = default)
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

        using var imageResources = ImageResources.Current is null ? new ImageResources() : null;
        using var fontResources = ConversionFontResources.Current is null ? new ConversionFontResources() : null;
        var pages = commands.GroupBy(command => command.PageNumber).OrderBy(page => page.Key).ToArray();
        if (pages.Length == 0)
        {
            using var output = outputFactory(1) ?? throw new InvalidOperationException("SVG の出力先を取得できません。");
            RenderPage([], pageSettings, output, buffering, cancellationToken);
            return;
        }

        foreach (var page in pages)
        {
            using var output = outputFactory(page.Key) ?? throw new InvalidOperationException("SVG の出力先を取得できません。");
            RenderPage(page, pageSettings, output, buffering, cancellationToken);
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
        => RenderPage(commands, pageSettings, output, new RenderBufferOptions());

    /// <summary>Renders one SVG page with a bounded intermediate buffer. The output remains open.</summary>
    /// <param name="commands">The commands, enumerated once.</param>
    /// <param name="pageSettings">The page dimensions in points.</param>
    /// <param name="output">The caller-owned output.</param>
    /// <param name="buffering">The intermediate buffer policy.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public void RenderPage(IEnumerable<DrawCommand> commands, PageSettings pageSettings, Stream output, RenderBufferOptions buffering, CancellationToken cancellationToken = default)
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

        using var imageResources = ImageResources.Current is null ? new ImageResources() : null;
        using var fontResources = ConversionFontResources.Current is null ? new ConversionFontResources() : null;
        using var buffer = new SpillableBufferStream(buffering);
        using var nativeWrites = new NativeWriteStream(buffer);
        using (var canvas = SKSvgCanvas.Create(
            SKRect.Create((float)pageSettings.Width, (float)pageSettings.Height),
            nativeWrites))
        {
            using var background = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill };
            canvas.DrawRect(0, 0, (float)pageSettings.Width, (float)pageSettings.Height, background);
            var drawingContext = new SkiaDrawingContext(textAsPaths: true, _fontManager);
            foreach (var command in commands)
            {
                cancellationToken.ThrowIfCancellationRequested();
                drawingContext.Execute(canvas, command);
                nativeWrites.ThrowIfFailed();
            }
        }

        nativeWrites.ThrowIfFailed();
        ConversionMetrics.Report("svgBufferBytes", buffer.Length);
        ConversionMetrics.Report("svgBufferMemoryPeakBytes", buffer.PeakMemoryCapacity);
        ConversionMetrics.Report("svg.spillCount", buffer.HasSpilled ? 1 : 0);
        cancellationToken.ThrowIfCancellationRequested();
        buffer.Position = 0;
        var normalizeTimer = System.Diagnostics.Stopwatch.StartNew();
        var readerSettings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreWhitespace = true,
        };
        using var cancellableInput = cancellationToken.CanBeCanceled ? new CancellationReadStream(buffer, cancellationToken) : null;
        using var reader = XmlReader.Create(cancellableInput ?? (Stream)buffer, readerSettings);
        if (reader.MoveToContent() != XmlNodeType.Element)
        {
            throw new InvalidDataException("SVG のルート要素がありません。");
        }

        var width = Format(pageSettings.Width);
        var height = Format(pageSettings.Height);

        var writerSettings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            CloseOutput = false,
            Indent = false,
        };
        using var writer = XmlWriter.Create(output, writerSettings);
        writer.WriteStartDocument();
        writer.WriteStartElement(reader.Prefix, reader.LocalName, reader.NamespaceURI);
        if (reader.MoveToFirstAttribute())
        {
            do
            {
                if (reader.NamespaceURI.Length != 0 || reader.LocalName is not ("width" or "height" or "viewBox"))
                {
                    writer.WriteAttributeString(reader.Prefix, reader.LocalName, reader.NamespaceURI, reader.Value);
                }
            }
            while (reader.MoveToNextAttribute());

            reader.MoveToElement();
        }

        writer.WriteAttributeString("width", width + "pt");
        writer.WriteAttributeString("height", height + "pt");
        writer.WriteAttributeString("viewBox", $"0 0 {width} {height}");
        if (!reader.IsEmptyElement)
        {
            reader.Read();
            while (!reader.EOF && !(reader.NodeType == XmlNodeType.EndElement && reader.Depth == 0))
            {
                // Copy each subtree without retaining an XDocument for the whole page.
                cancellationToken.ThrowIfCancellationRequested();
                writer.WriteNode(reader, defattr: false);
            }
        }

        writer.WriteEndElement();
        writer.WriteEndDocument();
        writer.Flush();
        ConversionMetrics.Report("svgNormalize.ms", normalizeTimer.Elapsed.TotalMilliseconds);
    }

    /// <summary>指定されたポイント寸法の単一キャンバスを SVG として出力します。</summary>
    /// <param name="commands">出力対象の描画コマンドです。</param>
    /// <param name="widthPoints">キャンバスの幅をポイント単位で指定します。</param>
    /// <param name="heightPoints">キャンバスの高さをポイント単位で指定します。</param>
    /// <param name="output">SVG を書き込むストリームです。</param>
    public void RenderCanvas(IEnumerable<DrawCommand> commands, double widthPoints, double heightPoints, Stream output)
    {
        RenderPage(commands, new PageSettings(widthPoints, heightPoints), output);
    }

    /// <summary>Renders a single SVG canvas using the specified intermediate buffer policy.</summary>
    /// <param name="commands">The commands, enumerated once.</param>
    /// <param name="widthPoints">The width in points.</param>
    /// <param name="heightPoints">The height in points.</param>
    /// <param name="output">The caller-owned output.</param>
    /// <param name="buffering">The intermediate buffer policy.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public void RenderCanvas(IEnumerable<DrawCommand> commands, double widthPoints, double heightPoints, Stream output, RenderBufferOptions buffering, CancellationToken cancellationToken = default) =>
        RenderPage(commands, new PageSettings(widthPoints, heightPoints), output, buffering, cancellationToken);

    private static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static void ValidateDimension(double value, string parameterName)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0 || value > float.MaxValue)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "ページ寸法は正の有限値で指定してください。");
        }
    }
}
