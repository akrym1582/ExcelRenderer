using ReportEngine.Drawing;
using ReportEngine.Layout;
using ReportEngine.Model;

namespace ReportEngine.Abstractions;

public interface IReportLayoutPass
{
    void Execute(ReportLayoutContext context);
}
