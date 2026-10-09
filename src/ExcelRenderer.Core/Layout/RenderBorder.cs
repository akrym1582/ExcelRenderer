using ExcelRenderer.Core.Model;

namespace ExcelRenderer.Core.Layout;

/// <summary>
/// レンダリング時の配置矩形と、その各辺へ適用する罫線設定を表します。
/// </summary>
internal sealed record RenderBorder(ReportRect Bounds, BorderStyle Border);
