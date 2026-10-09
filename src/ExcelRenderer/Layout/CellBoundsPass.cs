using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>
/// 列幅と行高から各セルおよび結合セルの罫線の配置矩形を算出します。
/// </summary>
public sealed class CellBoundsPass : IReportLayoutPass
{
    /// <summary>
    /// 列と行の配置情報を合算して各セルの矩形を求め、結合セルの部分罫線とともにコンテキストへ格納します。
    /// </summary>
    /// <param name="context">入力シート、計測機能、および各工程の計算結果を保持するレイアウトコンテキストです。</param>
    public void Execute(ReportLayoutContext context)
    {
        var core = CoreIntegration.CoreLayoutContextAdapter.CreateMeasured(context);
        new Core.Layout.CellBoundsPass().Execute(core);
        CoreIntegration.CoreLayoutContextAdapter.SyncCellBounds(core, context);
    }

    /// <summary>Projects lightweight common bounds without duplicating the geometry algorithm.</summary>
    /// <param name="context">The original public geometry context.</param>
    /// <returns>The original ordered addresses and bounds.</returns>
    internal static IEnumerable<(CellAddress Address, ReportRect Bounds)> EnumerateBounds(ReportLayoutContext context) =>
        Core.Layout.CellBoundsPass.EnumerateBounds(CoreIntegration.CoreLayoutContextAdapter.CreateGeometry(context)).Select(item => (CoreIntegration.CoreModelAdapter.ToPublic(item.Address), CoreIntegration.CoreModelAdapter.ToPublic(item.Bounds)));
}
