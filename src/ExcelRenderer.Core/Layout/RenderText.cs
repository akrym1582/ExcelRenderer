using ExcelRenderer.Core.Abstractions;
using ExcelRenderer.Core.Model;

namespace ExcelRenderer.Core.Layout;

/// <summary>
/// ページ上に配置する文字列、その描画矩形、およびセルスタイルを表します。
/// </summary>
internal sealed record RenderText(ReportRect Bounds, string Text, CellStyle Style);
