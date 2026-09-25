using ExcelRenderer.Drawing;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;

namespace ExcelRenderer.Abstractions;

/// <summary>
/// IReportLayoutPass が表すデータと操作を提供します.
/// </summary>
public interface IReportLayoutPass
{
    /// <summary>
    /// Execute を実行します.
    /// </summary>
    /// <param name="context">context に渡す値です。</param>
    void Execute(ReportLayoutContext context);
}
