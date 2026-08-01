using ReportEngine.Drawing;
using ReportEngine.Layout;
using ReportEngine.Model;

namespace ReportEngine.Abstractions;

public interface ITextMeasurer
{
    TextSize Measure(string text, FontStyle font, double availableWidth, bool wrap);
}
