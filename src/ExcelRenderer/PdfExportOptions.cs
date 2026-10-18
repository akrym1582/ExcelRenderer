using System.Threading;
using System.Threading.Tasks;
using ExcelRenderer.Drawing;
using ExcelRenderer.Excel;
using ExcelRenderer.Fonts;
using ExcelRenderer.Layout;
using ExcelRenderer.Markdown;
using ExcelRenderer.Model;
using ExcelRenderer.Rendering;
using ExcelRenderer.SkiaSharp;

namespace ExcelRenderer;

/// <summary>Excel ワークシートを PDF 文書へ出力するときの設定を表します。</summary>
public sealed record PdfExportOptions
{
    /// <summary>Gets the PDF drawing backend; nondefault values require PDF output.</summary>
    public PdfBackend PdfBackend { get; init; } = global::ExcelRenderer.PdfBackend.SkiaSharp;

    /// <summary>Gets the workbook formula display policy.</summary>
    public ExcelRenderer.Excel.ExcelReadOptions ReadOptions { get; init; } = new();

    /// <summary>Gets the cell hyperlink preservation mode.</summary>
    public HyperlinkMode Hyperlinks { get; init; } = HyperlinkMode.Preserve;

    /// <summary>Gets the worksheet name. 出力対象のワークシート名を取得します。<see langword="null"/> の場合はすべてのワークシートを出力します。</summary>
    public string? SheetName { get; init; }

    /// <summary>Gets the font selection criteria. 描画に使用するフォントの検索設定を取得します。</summary>
    public FontOptions FontOptions { get; init; } = new();
}
