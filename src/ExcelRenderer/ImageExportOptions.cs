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

/// <summary>
/// ImageExportOptions が表すデータと操作を提供します.
/// </summary>
public sealed record ImageExportOptions
{
    /// <summary>
    /// Gets the value. 対応する値を取得または設定します.
    /// </summary>
    public string? SheetName { get; init; }

    /// <summary>
    /// Gets the value. 対応する値を取得または設定します.
    /// </summary>
    public int Dpi { get; init; } = 144;
}
