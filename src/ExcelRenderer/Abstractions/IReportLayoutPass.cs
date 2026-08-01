using ExcelRenderer.Drawing;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;

namespace ExcelRenderer.Abstractions;

public interface IReportLayoutPass
{
    void Execute(ReportLayoutContext context);
}
