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
        var x = 0d;
        foreach (var column in context.VisibleColumns)
        {
            var width = context.Sheet.Columns.GetValueOrDefault(column, new()).Width;
            context.ColumnLayouts[column] = new(column, x, width);
            x += width;
        }
    }
}
