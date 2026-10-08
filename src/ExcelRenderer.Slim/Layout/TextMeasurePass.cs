using ExcelRenderer.Slim.Abstractions;
using ExcelRenderer.Slim.Model;

namespace ExcelRenderer.Slim.Layout;

/// <summary>
/// 各セルの結合幅と折り返し設定を考慮して、セル文字列の描画寸法を計測します。
/// </summary>
internal sealed class TextMeasurePass : IReportLayoutPass
{
    private const double CellTextPadding = 0.5;

    /// <summary>
    /// 各セルが占有する列幅を合算し、フォントと折り返し設定を適用した文字列寸法をコンテキストへ格納します。
    /// </summary>
    /// <param name="context">入力シート、計測機能、および各工程の計算結果を保持するレイアウトコンテキストです。</param>
    public void Execute(ReportLayoutContext context)
    {
        var count = 0;
        foreach (var (address, cell) in context.CandidateCells)
        {
            if ((count++ & 255) == 0)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
            }

            if (string.IsNullOrEmpty(cell.Text))
            {
                continue;
            }

            if (!context.ColumnLayouts.TryGetValue(address.Column, out var column))
            {
                continue;
            }

            if (context.RowLayouts.Count > 0 && !context.RowLayouts.ContainsKey(address.Row))
            {
                continue;
            }

            var availableWidth = Enumerable.Range(address.Column, cell.ColumnSpan)
                .Where(context.ColumnLayouts.ContainsKey).Sum(x => context.ColumnLayouts[x].Width);
            availableWidth = CellContentBounds.Calculate(new(0, 0, availableWidth, double.MaxValue), cell.Style).Width;
            if (context.TextMeasurer is ITextLayoutService layoutService)
            {
                var text = GetLayoutText(cell.Text ?? string.Empty, cell.Style);
                var layout = layoutService.Layout(text, cell.Style.Font, availableWidth, cell.Style.WrapText);
                if (cell.Style.ShrinkToFit && !cell.Style.WrapText && layout.Size.Width > availableWidth &&
                    layout.Size.Width > 0)
                {
                    layout = TextLayoutTransform.Scale(
                        layout with { EffectiveFontSize = cell.Style.Font.Size },
                        availableWidth / layout.Size.Width);
                }
                else if (!layout.HasExplicitEffectiveFontSize || layout.EffectiveFontSize != cell.Style.Font.Size)
                {
                    layout = layout with { EffectiveFontSize = cell.Style.Font.Size };
                }

                context.TextLayouts[address] = layout;
                context.TextSizes[address] = layout.Size;
            }
            else
            {
                context.TextSizes[address] = context.TextMeasurer.Measure(
                    GetLayoutText(cell.Text ?? string.Empty, cell.Style), cell.Style.Font, availableWidth, cell.Style.WrapText);
            }
        }
    }

    private static string GetLayoutText(string text, CellStyle style) => text;
}
