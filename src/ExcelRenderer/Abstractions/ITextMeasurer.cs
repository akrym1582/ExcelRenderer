using ExcelRenderer.Drawing;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;

namespace ExcelRenderer.Abstractions;

/// <summary>
/// 指定されたフォントと折り返し条件で文字列の描画寸法を計測する機能を定義します。
/// </summary>
public interface ITextMeasurer
{
    /// <summary>
    /// 文字列を指定されたフォントと幅制約で描画したときに必要となる寸法を計測します。
    /// </summary>
    /// <param name="text">描画寸法を計測する文字列です。</param>
    /// <param name="font">文字列の書体、サイズ、および文字装飾を指定するフォント設定です。</param>
    /// <param name="availableWidth">文字列を配置できる最大幅です。</param>
    /// <param name="wrap">最大幅を超える文字列を折り返して計測する場合は <see langword="true"/> です。</param>
    /// <returns>計測した文字列の幅と高さを返します。</returns>
    TextSize Measure(string text, FontStyle font, double availableWidth, bool wrap);
}
