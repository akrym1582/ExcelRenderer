using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>
/// NormalizePass が表すデータと操作を提供します.
/// </summary>
public sealed class NormalizePass : IReportLayoutPass
{
    /// <summary>
    /// Execute を実行します.
    /// </summary>
    /// <param name="context">context に渡す値です。</param>
    public void Execute(ReportLayoutContext context)
    {
        // ExcelReader represents merged ranges on their top-left cell; this pass
        // remains the explicit normalization boundary for additional input providers.
    }
}
