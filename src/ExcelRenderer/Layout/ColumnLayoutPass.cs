using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>
/// 表示対象の列を左から順に並べ、各列の水平位置と幅を算出します。
/// </summary>
public sealed class ColumnLayoutPass : IReportLayoutPass
{
    /// <summary>
    /// 表示対象列を左から走査し、累積した列幅から各列の水平位置を求めてコンテキストへ格納します。
    /// </summary>
    /// <param name="context">入力シート、計測機能、および各工程の計算結果を保持するレイアウトコンテキストです。</param>
    public void Execute(ReportLayoutContext context)
    {
        var core = CoreIntegration.CoreLayoutContextAdapter.CreateGeometry(context);
        new Core.Layout.ColumnLayoutPass().Execute(core);
        foreach (var column in core.ColumnLayouts)
        {
            context.ColumnLayouts[column.Key] = new(column.Value.Column, column.Value.X, column.Value.Width);
        }
    }
}
