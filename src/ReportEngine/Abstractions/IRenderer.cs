using ReportEngine.Drawing;
using ReportEngine.Layout;
using ReportEngine.Model;

namespace ReportEngine.Abstractions;

public interface IRenderer
{
    void Render(IReadOnlyList<DrawCommand> commands, PageSettings pageSettings, Stream output);
}
