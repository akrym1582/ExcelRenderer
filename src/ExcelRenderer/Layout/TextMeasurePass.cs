using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>
/// 各セルの結合幅と折り返し設定を考慮して、セル文字列の描画寸法を計測します。
/// </summary>
public sealed class TextMeasurePass : IReportLayoutPass
{
    /// <summary>
    /// 各セルが占有する列幅を合算し、フォントと折り返し設定を適用した文字列寸法をコンテキストへ格納します。
    /// </summary>
    /// <param name="context">入力シート、計測機能、および各工程の計算結果を保持するレイアウトコンテキストです。</param>
    public void Execute(ReportLayoutContext context)
    {
        foreach (var (address, cell) in context.Sheet.Cells)
        {
            if (!context.ColumnLayouts.TryGetValue(address.Column, out var column))
            {
                continue;
            }

            var availableWidth = Enumerable.Range(address.Column, cell.ColumnSpan)
                .Where(context.ColumnLayouts.ContainsKey).Sum(x => context.ColumnLayouts[x].Width);
            context.TextSizes[address] = context.TextMeasurer.Measure(
                cell.Text ?? string.Empty, cell.Style.Font, availableWidth, cell.Style.WrapText);
        }
    }
}
