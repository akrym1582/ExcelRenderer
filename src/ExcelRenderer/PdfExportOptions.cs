using System.Threading;
using System.Threading.Tasks;
using ExcelRenderer.Drawing;
using ExcelRenderer.Excel;
using ExcelRenderer.Fonts;
using ExcelRenderer.Layout;
using ExcelRenderer.Markdown;
using ExcelRenderer.Model;
using ExcelRenderer.PdfSharp;
using ExcelRenderer.SkiaSharp;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace ExcelRenderer;

/// <summary>Excel ワークシートを PDF 文書へ出力するときの設定を表します。</summary>
public sealed record PdfExportOptions
{
    /// <summary>Gets the worksheet name. 出力対象のワークシート名を取得します。<see langword="null"/> の場合はすべてのワークシートを出力します。</summary>
    public string? SheetName { get; init; }
}
