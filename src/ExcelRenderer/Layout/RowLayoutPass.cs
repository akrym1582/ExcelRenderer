using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>
/// RowLayoutPass が表すデータと操作を提供します.
/// </summary>
public sealed class RowLayoutPass : IReportLayoutPass
{
    /// <summary>
    /// Execute を実行します.
    /// </summary>
    /// <param name="context">context に渡す値です。</param>
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
