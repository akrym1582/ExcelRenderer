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
        foreach (var (address, bounds) in EnumerateBounds(context))
        {
            var cell = context.Sheet.Cells[address];
            context.CellLayouts[address] = new(
                address,
                bounds,
                context.TextSizes.GetValueOrDefault(address))
            {
                ContentBounds = CellContentBounds.Calculate(bounds, cell.Style),
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

    /// <summary>Enumerates lightweight cell geometry without style, border, or text layout copies.</summary>
    /// <param name="context">The geometry and optional page candidates.</param>
    /// <returns>Visible source addresses and point bounds in stable model order.</returns>
    internal static IEnumerable<(CellAddress Address, ReportRect Bounds)> EnumerateBounds(ReportLayoutContext context)
    {
        var count = 0;
        foreach (var (address, cell) in context.CandidateCells)
        {
            if ((count++ & 255) == 0)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
            }

            if (context.Sheet.RequestedRange is { } selected && !selected.Contains(address) &&
                !context.Sheet.MergedRanges.Any(range => range.First == address && range.First.Row <= selected.Last.Row &&
                    range.Last.Row >= selected.First.Row && range.First.Column <= selected.Last.Column && range.Last.Column >= selected.First.Column))
            {
                continue;
            }

            if (!context.ColumnLayouts.TryGetValue(address.Column, out var column) ||
                !context.RowLayouts.TryGetValue(address.Row, out var row))
            {
                continue;
            }

            var width = 0d;
            for (var index = address.Column; index < address.Column + cell.ColumnSpan; index++)
            {
                if (context.ColumnLayouts.TryGetValue(index, out var metric))
                {
                    width += metric.Width;
                }
            }

            var height = 0d;
            for (var index = address.Row; index < address.Row + cell.RowSpan; index++)
            {
                if (context.RowLayouts.TryGetValue(index, out var metric))
                {
                    height += metric.Height;
                }
            }

            if (width <= 0 || height <= 0)
            {
                continue;
            }

            yield return (address, new(column.X, row.Y, width, height));
        }
    }
}
