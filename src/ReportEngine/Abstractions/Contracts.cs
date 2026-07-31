using ReportEngine.Drawing;
using ReportEngine.Layout;
using ReportEngine.Model;

namespace ReportEngine.Abstractions;

public interface ITextMeasurer
{
    TextSize Measure(string text, FontStyle font, double availableWidth, bool wrap);
}

public interface IReportLayoutPass
{
    void Execute(ReportLayoutContext context);
}

public interface IRenderer
{
    void Render(IReadOnlyList<DrawCommand> commands, PageSettings pageSettings, Stream output);
}

public readonly record struct TextSize(double Width, double Height);
