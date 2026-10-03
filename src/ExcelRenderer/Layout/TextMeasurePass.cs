using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>
/// 各セルの結合幅と折り返し設定を考慮して、セル文字列の描画寸法を計測します。
/// </summary>
public sealed class TextMeasurePass : IReportLayoutPass
{
    private const double CellTextPadding = 0.5;

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
            availableWidth = CellContentBounds.Calculate(new(0, 0, availableWidth, double.MaxValue), cell.Style).Width;
            if (context.TextMeasurer is ITextLayoutService layoutService)
            {
                var text = GetLayoutText(cell.Text ?? string.Empty, cell.Style);
                var layout = layoutService.Layout(text, cell.Style.Font, availableWidth, cell.Style.WrapText);
                if (cell.Style.ShrinkToFit && !cell.Style.WrapText && layout.Size.Width > availableWidth &&
                    layout.Size.Width > 0)
                {
                    layout = ScaleLayout(layout, availableWidth / layout.Size.Width, cell.Style.Font.Size);
                }
                else
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

    private static string GetLayoutText(string text, CellStyle style)
    {
        if (!style.TopToBottom && style.TextRotation != 255)
        {
            return text;
        }

        var elements = System.Globalization.StringInfo.GetTextElementEnumerator(text);
        var result = new List<string>();
        while (elements.MoveNext())
        {
            result.Add(elements.GetTextElement());
        }

        return string.Join("\n", result);
    }

    private static TextLayoutResult ScaleLayout(TextLayoutResult layout, double scale, double fontSize) => new(
        new(layout.Size.Width * scale, layout.Size.Height * scale),
        layout.Lines.Select(line => line with
        {
            Width = line.Width * scale,
            Height = line.Height * scale,
            Baseline = line.Baseline * scale,
            Ascent = line.Ascent * scale,
            Descent = line.Descent * scale,
            Leading = line.Leading * scale,
            Runs = line.Runs.Select(run => run with
            {
                X = run.X * scale,
                Advance = run.Advance * scale,
            }).ToArray(),
        }).ToArray())
    {
        EffectiveFontSize = fontSize * scale,
    };
}
