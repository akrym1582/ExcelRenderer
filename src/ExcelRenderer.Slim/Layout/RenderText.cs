using ExcelRenderer.Slim.Abstractions;
using ExcelRenderer.Slim.Model;

namespace ExcelRenderer.Slim.Layout;

/// <summary>
/// ページ上に配置する文字列、その描画矩形、およびセルスタイルを表します。
/// </summary>
internal sealed record RenderText(ReportRect Bounds, string Text, CellStyle Style);
