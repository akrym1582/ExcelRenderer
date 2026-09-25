using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>
/// レンダリング時の配置矩形と、その各辺へ適用する罫線設定を表します。
/// </summary>
public sealed record RenderBorder(ReportRect Bounds, BorderStyle Border);
