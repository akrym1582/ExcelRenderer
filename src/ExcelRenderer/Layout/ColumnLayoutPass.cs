using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>
/// ColumnLayoutPass が表すデータと操作を提供します.
/// </summary>
public sealed class ColumnLayoutPass : IReportLayoutPass
{
    /// <summary>
    /// Execute を実行します.
    /// </summary>
    /// <param name="context">context に渡す値です。</param>
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
