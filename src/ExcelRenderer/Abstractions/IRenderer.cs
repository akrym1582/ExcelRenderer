using ExcelRenderer.Drawing;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;

namespace ExcelRenderer.Abstractions;

public interface IRenderer
{
    void Render(IReadOnlyList<DrawCommand> commands, PageSettings pageSettings, Stream output);
}
