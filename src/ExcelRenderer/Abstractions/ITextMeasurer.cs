using ExcelRenderer.Drawing;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;

namespace ExcelRenderer.Abstractions;

/// <summary>
/// ITextMeasurer が表すデータと操作を提供します.
/// </summary>
public interface ITextMeasurer
{
    /// <summary>
    /// Measure を実行します.
    /// </summary>
    /// <param name="text">text に渡す値です。</param>
    /// <param name="font">font に渡す値です。</param>
    /// <param name="availableWidth">availableWidth に渡す値です。</param>
    /// <param name="wrap">wrap に渡す値です。</param>
    /// <returns>処理によって得られた結果を返します。</returns>
    TextSize Measure(string text, FontStyle font, double availableWidth, bool wrap);
}
