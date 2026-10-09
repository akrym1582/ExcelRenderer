using System.Diagnostics;
using System.Globalization;
using ExcelRenderer.Abstractions;
using ExcelRenderer.Drawing;
using ExcelRenderer.Fonts;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using ExcelRenderer.Rendering;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using SkiaSharp;

namespace ExcelRenderer.PdfSharp;

/// <summary>ページ別の描画コマンドを PDFsharp で描画し、PDF 文書として出力します。</summary>
public sealed class PdfSharpRenderer : IRenderer
{
    private readonly IFontManager? fontManager;

    /// <summary>Initializes a new instance of the <see cref="PdfSharpRenderer"/> class.</summary>
    public PdfSharpRenderer()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PdfSharpRenderer"/> class.</summary>
    /// <param name="fontManager">The font manager shared with layout and diagnostics.</param>
    public PdfSharpRenderer(IFontManager fontManager)
    {
        this.fontManager = fontManager ?? throw new ArgumentNullException(nameof(fontManager));
    }

    /// <summary>Gets the callback for image warnings, including decode failure details. Warnings are also written to trace listeners.</summary>
    public Action<ConversionDiagnostic>? DiagnosticHandler { get; init; }

    /// <summary>Gets or sets the diagnostic context for the page being appended.</summary>
    internal Action<ConversionDiagnostic>? PageDiagnosticHandler { get; set; }

    /// <summary>Gets or sets the product page-completion callback.</summary>
    internal Action<global::PdfSharp.Pdf.PdfPage>? PageCompleted { get; set; }

    /// <summary>描画コマンドをページ番号ごとに描画し、すべてのページを含む PDF 文書を出力します。</summary>
    /// <param name="commands">背景、罫線、文字、画像、および図形をページ上へ配置する描画コマンドです。</param>
    /// <param name="pageSettings">各 PDF ページに適用する幅と高さを含むページ設定です。</param>
    /// <param name="output">生成した PDF 文書を書き込むストリームです。</param>
    public void Render(IReadOnlyList<DrawCommand> commands, PageSettings pageSettings, Stream output)
    {
        using var fontSession = RenderResourceSession.Current?.FontSession is null ? Core.Rendering.PdfSharpFontGate.Acquire() : null;
        GlobalFontSettings.FontResolver ??= fontManager is null ? new PdfSharpFontResolver() : new PdfSharpFontResolver(fontManager);
        using var imageResources = ImageResources.Current is null ? new ImageResources() : null;
        using var document = new PdfDocument();
        var pages = commands.GroupBy(x => x.PageNumber).OrderBy(x => x.Key).ToArray();
        if (pages.Length == 0)
        {
            CreateRenderer().AppendPage(document, CoreIntegration.CoreModelAdapter.ToCore(pageSettings), []);
        }

        foreach (var pageCommands in pages)
        {
            CreateRenderer().AppendPage(document, CoreIntegration.CoreModelAdapter.ToCore(pageSettings), pageCommands.Select(CoreIntegration.CoreCommandAdapter.CreateCoreProjection()));
        }

        Core.Pdf.CorePdfRenderer.Save(document, output);
    }

    /// <summary>Appends exactly one page, including pages with no commands.</summary>
    /// <param name="document">The final document owned by the caller.</param>
    /// <param name="pageSettings">The output page dimensions.</param>
    /// <param name="commands">The commands on this page.</param>
    internal void AppendPage(PdfDocument document, PageSettings pageSettings, IEnumerable<DrawCommand> commands) =>
        CreateRenderer().AppendPage(document, CoreIntegration.CoreModelAdapter.ToCore(pageSettings), commands.Select(CoreIntegration.CoreCommandAdapter.CreateCoreProjection()));

    private CoreIntegration.FullPdfRenderer CreateRenderer() => new(fontManager)
    {
        PageCompleted = PageCompleted,
        DiagnosticHandler = diagnostic => DiagnosticHandler?.Invoke(CoreIntegration.CoreDiagnosticAdapter.ToPublic(diagnostic)),
        PageDiagnosticHandler = diagnostic => PageDiagnosticHandler?.Invoke(CoreIntegration.CoreDiagnosticAdapter.ToPublic(diagnostic)),
    };
}
