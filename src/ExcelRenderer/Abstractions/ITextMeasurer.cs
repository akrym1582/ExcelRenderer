using ExcelRenderer.Drawing;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;

namespace ExcelRenderer.Abstractions;

public interface ITextMeasurer
{
    TextSize Measure(string text, FontStyle font, double availableWidth, bool wrap);
}
