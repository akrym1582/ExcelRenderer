using ExcelRenderer.Model;

namespace ExcelRenderer.Markdown;

/// <summary>
/// ワークシートのセル定義を、実際の行高と列幅を反映した表示セルへ変換します。
/// </summary>
public sealed class VisualCellBuilder
{
    /// <summary>
    /// 非表示の行または列だけに含まれるセルを除外し、結合範囲を含む各セルの表示位置と寸法を計算します。
    /// </summary>
    /// <param name="sheet">セル、行高、および列幅を保持する変換元ワークシート。</param>
    /// <returns>セルアドレス順に並び、ワークシート左上を原点とする座標と寸法を持つ表示セルの一覧。</returns>
    public IReadOnlyList<VisualCell> Build(ReportSheet sheet)
    {
        var result = new List<VisualCell>();
        var metrics = new Layout.SheetGeometry(sheet);
        foreach (var entry in sheet.Cells.OrderBy(x => x.Key.Row).ThenBy(x => x.Key.Column))
        {
            var address = entry.Key;
            var cell = entry.Value;
            if (IsHidden(sheet, address, cell))
            {
                continue;
            }

            var range = new CellRange(
                address,
                new(address.Row + cell.RowSpan - 1, address.Column + cell.ColumnSpan - 1));
            result.Add(new(
                range,
                cell.Text,
                metrics.ColumnStart(address.Column),
                metrics.RowStart(address.Row),
                metrics.ColumnStart(range.Last.Column + 1) - metrics.ColumnStart(range.First.Column),
                metrics.RowStart(range.Last.Row + 1) - metrics.RowStart(range.First.Row),
                cell.Style,
                cell.Formula));
        }

        return result;
    }

    /// <summary>
    /// ワークシート左端から指定列の左端までの表示距離を求めます。
    /// </summary>
    /// <param name="sheet">列幅の定義を保持するワークシート。</param>
    /// <param name="column">左端位置を求める 1 始まりの列番号。</param>
    /// <returns>指定列より前にある非表示でない列の幅を合計した X 座標。</returns>
    internal static double OffsetX(ReportSheet sheet, int column) =>
        new Layout.SheetGeometry(sheet).ColumnStart(column);

    /// <summary>
    /// ワークシート上端から指定行の上端までの表示距離を求めます。
    /// </summary>
    /// <param name="sheet">行高の定義を保持するワークシート。</param>
    /// <param name="row">上端位置を求める 1 始まりの行番号。</param>
    /// <returns>指定行より前にある非表示でない行の高さを合計した Y 座標。</returns>
    internal static double OffsetY(ReportSheet sheet, int row) =>
        new Layout.SheetGeometry(sheet).RowStart(row);

    private static bool IsHidden(ReportSheet s, CellAddress a, ReportCell cell) =>
        Enumerable.Range(a.Column, cell.ColumnSpan).All(c => s.Columns.TryGetValue(c, out var d) && d.IsHidden) ||
        Enumerable.Range(a.Row, cell.RowSpan).All(r => s.Rows.GetValueOrDefault(r, new(s.DefaultRowHeight, s.DefaultRowsHidden)).IsHidden);
}
