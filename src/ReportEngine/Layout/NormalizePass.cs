using ReportEngine.Abstractions;
using ReportEngine.Model;

namespace ReportEngine.Layout;

public sealed class NormalizePass : IReportLayoutPass
{
    public void Execute(ReportLayoutContext context)
    {
        // ExcelReader represents merged ranges on their top-left cell; this pass
        // remains the explicit normalization boundary for additional input providers.
    }
}
