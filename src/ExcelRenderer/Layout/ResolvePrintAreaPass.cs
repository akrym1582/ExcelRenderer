using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>
/// 明示された印刷範囲を採用し、未指定の場合はセル、画像、および図形から使用範囲を求めます。
/// </summary>
public sealed class ResolvePrintAreaPass : IReportLayoutPass
{
    /// <summary>
    /// 明示された印刷範囲を採用し、未指定の場合は内容が存在するセル、画像、および図形を包含する範囲を設定します。
    /// </summary>
    /// <param name="context">入力シート、計測機能、および各工程の計算結果を保持するレイアウトコンテキストです。</param>
    public void Execute(ReportLayoutContext context)
    {
        context.PrintArea = context.Sheet.PrintArea ?? GetUsedRange(context.Sheet);
    }

    private static CellRange? GetUsedRange(ReportSheet sheet)
    {
        var addresses = sheet.Cells.Keys
            .Concat((sheet.Images ?? []).Select(image => image.Anchor))
            .Concat((sheet.Shapes ?? []).Select(shape => shape.Anchor))
            .ToArray();
        if (addresses.Length == 0)
        {
            return null;
        }

        var lastRows = sheet.Cells
            .Select(cell => cell.Key.Row + cell.Value.RowSpan - 1)
            .Concat((sheet.Images ?? []).Select(image => image.Anchor.Row));
        lastRows = lastRows.Concat((sheet.Shapes ?? []).Select(shape => shape.Anchor.Row));
        var lastColumns = sheet.Cells
            .Select(cell => cell.Key.Column + cell.Value.ColumnSpan - 1)
            .Concat((sheet.Images ?? []).Select(image => image.Anchor.Column));
        lastColumns = lastColumns.Concat((sheet.Shapes ?? []).Select(shape => shape.Anchor.Column));
        return new CellRange(
            new(addresses.Min(address => address.Row), addresses.Min(address => address.Column)),
            new(lastRows.Max(), lastColumns.Max()));
    }
}
