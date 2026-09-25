using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>
/// 列幅と行高から各セルおよび結合セルの罫線の配置矩形を算出します。
/// </summary>
public sealed class CellBoundsPass : IReportLayoutPass
{
    /// <summary>
    /// 列と行の配置情報を合算して各セルの矩形を求め、結合セルの部分罫線とともにコンテキストへ格納します。
    /// </summary>
    /// <param name="context">入力シート、計測機能、および各工程の計算結果を保持するレイアウトコンテキストです。</param>
    public void Execute(ReportLayoutContext context)
    {
        foreach (var (address, cell) in context.Sheet.Cells)
        {
            if (!context.ColumnLayouts.TryGetValue(address.Column, out var column) ||
                !context.RowLayouts.TryGetValue(address.Row, out var row))
            {
                continue;
            }

            var width = Enumerable.Range(address.Column, cell.ColumnSpan)
                .Where(context.ColumnLayouts.ContainsKey).Sum(x => context.ColumnLayouts[x].Width);
            var height = Enumerable.Range(address.Row, cell.RowSpan)
                .Where(context.RowLayouts.ContainsKey).Sum(x => context.RowLayouts[x].Height);
            context.CellLayouts[address] = new(
                address,
                new(column.X, row.Y, width, height),
                context.TextSizes.GetValueOrDefault(address))
            {
                MergedBorders = cell.MergedBorders?
                    .Where(border => context.ColumnLayouts.ContainsKey(border.Address.Column) &&
                        context.RowLayouts.ContainsKey(border.Address.Row))
                    .Select(border =>
                    {
                        var borderColumn = context.ColumnLayouts[border.Address.Column];
                        var borderRow = context.RowLayouts[border.Address.Row];
                        return new RenderBorder(new(borderColumn.X, borderRow.Y, borderColumn.Width, borderRow.Height), border.Border);
                    }).ToArray(),
            };
        }
    }
}
