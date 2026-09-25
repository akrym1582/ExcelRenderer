using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>
/// ページ上に配置する文字列、その描画矩形、およびセルスタイルを表します。
/// </summary>
public sealed record RenderText(ReportRect Bounds, string Text, CellStyle Style);
