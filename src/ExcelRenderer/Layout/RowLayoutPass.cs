using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>
/// 表示対象の行を上から順に並べ、各行の垂直位置と高さを算出します。
/// </summary>
public sealed class RowLayoutPass : IReportLayoutPass
{
    /// <summary>
    /// 表示対象行を上から走査し、累積した行高から各行の垂直位置を求めてコンテキストへ格納します。
    /// </summary>
    /// <param name="context">入力シート、計測機能、および各工程の計算結果を保持するレイアウトコンテキストです。</param>
    public void Execute(ReportLayoutContext context)
    {
        var y = 0d;
        foreach (var row in context.VisibleRows)
        {
            var height = context.Sheet.Rows.GetValueOrDefault(row, new()).Height;
            context.RowLayouts[row] = new(row, y, height);
            y += height;
        }
    }
}
