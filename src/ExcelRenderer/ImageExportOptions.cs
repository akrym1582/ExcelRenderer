using System.Threading;
using System.Threading.Tasks;
using ExcelRenderer.Drawing;
using ExcelRenderer.Excel;
using ExcelRenderer.Fonts;
using ExcelRenderer.Layout;
using ExcelRenderer.Markdown;
using ExcelRenderer.Model;
using ExcelRenderer.SkiaSharp;

namespace ExcelRenderer;

/// <summary>Excel ワークシートを PNG 画像へ出力するときの設定を表します。</summary>
public sealed record ImageExportOptions
{
    /// <summary>Gets the workbook formula display policy.</summary>
    public ExcelRenderer.Excel.ExcelReadOptions ReadOptions { get; init; } = new();

    /// <summary>Gets one worksheet name. Use SheetNames for multiple sheets; leave both null to export all worksheets.</summary>
    public string? SheetName { get; init; }

    /// <summary>Gets the worksheet names in output order, with duplicates removed. Null uses SheetName or all worksheets. Cannot be combined with SheetName.</summary>
    public IReadOnlyList<string>? SheetNames { get; init; }

    /// <summary>Gets the PNG resolution. 出力する PNG 画像の解像度を DPI 単位で取得します。</summary>
    public int Dpi { get; init; } = 144;

    /// <summary>Gets the font selection criteria. 描画に使用するフォントの検索設定を取得します。</summary>
    public FontOptions FontOptions { get; init; } = new();
}
