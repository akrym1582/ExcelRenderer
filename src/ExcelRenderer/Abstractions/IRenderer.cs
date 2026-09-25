using ExcelRenderer.Drawing;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;

namespace ExcelRenderer.Abstractions;

/// <summary>
/// IRenderer が表すデータと操作を提供します.
/// </summary>
public interface IRenderer
{
    /// <summary>
    /// Render を実行します.
    /// </summary>
    /// <param name="commands">commands に渡す値です。</param>
    /// <param name="pageSettings">pageSettings に渡す値です。</param>
    /// <param name="output">output に渡す値です。</param>
    void Render(IReadOnlyList<DrawCommand> commands, PageSettings pageSettings, Stream output);
}
