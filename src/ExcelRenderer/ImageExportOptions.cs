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

/// <summary>Excel ワークシートを PNG 画像へ出力するときの設定を表します。</summary>
public sealed record ImageExportOptions
{
    /// <summary>Gets the worksheet name. 出力対象のワークシート名を取得します。<see langword="null"/> の場合はすべてのワークシートを出力します。</summary>
    public string? SheetName { get; init; }

    /// <summary>Gets the PNG resolution. 出力する PNG 画像の解像度を DPI 単位で取得します。</summary>
    public int Dpi { get; init; } = 144;
}
