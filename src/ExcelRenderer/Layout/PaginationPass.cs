using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>
/// 印刷範囲を用紙サイズに分割し、拡大縮小、余白、印刷タイトル、およびヘッダー・フッターを反映したページを生成します。
/// </summary>
public sealed class PaginationPass : IReportLayoutPass
{
    /// <summary>
    /// 印刷可能領域に合わせてセル、画像、および図形をページへ分割し、印刷タイトルとヘッダー・フッターを配置します。
    /// </summary>
    /// <param name="context">入力シート、計測機能、および各工程の計算結果を保持するレイアウトコンテキストです。</param>
    public void Execute(ReportLayoutContext context)
    {
        var plans = Plan(context);
        context.RenderDocument = new(plans.Select((plan, index) => Materialize(context, plan, index + 1, plans.Count)).ToArray());
    }

    /// <summary>Builds a single planned page and resolves its header and footer.</summary>
    /// <param name="context">The context used by this operation.</param>
    /// <param name="plan">The plan used by this operation.</param>
    /// <param name="number">The number used by this operation.</param>
    /// <param name="count">The count used by this operation.</param>
    /// <returns>The planned or generated result.</returns>
    internal static RenderPage Materialize(ReportLayoutContext context, PaginationPagePlan plan, int number, int count)
    {
        var page = plan.Horizontal is null || plan.Vertical is null
            ? new RenderPage(number, [])
            : new RenderPageBuilder(context, plan.BodyColumns, plan.BodyRows, plan.TitleColumns, plan.TitleRows, plan.TitleColumnEnd, plan.TitleRowEnd, plan.TitleWidth, plan.TitleHeight, plan.Scale)
                .Build(number, plan.Horizontal.Value, plan.Vertical.Value);
        return page with { HeaderFooterTexts = HeaderFooterLayout.Create(context.Sheet, number, count) };
    }

    /// <summary>Plans page bands without measuring cell text.</summary>
    /// <param name="context">The context used by this operation.</param>
    /// <returns>The planned or generated result.</returns>
    internal static IReadOnlyList<PaginationPagePlan> Plan(ReportLayoutContext context)
    {
        if (context.PrintArea is not { } ||
            (context.Sheet.RequestedRange is null && context.Sheet.Hyperlinks.Count == 0 && !context.Sheet.Cells.Keys.Any(address => context.ColumnLayouts.ContainsKey(address.Column) && context.RowLayouts.ContainsKey(address.Row)) && (context.Sheet.Images?.Count ?? 0) == 0 && (context.Sheet.Shapes?.Count ?? 0) == 0))
        {
            return HeaderFooterLayout.Create(context.Sheet, 1, 1).Count == 0 ? [] : [new(null, null, [], [], [], [], 0, 0, 0, 0, 1)];
        }

        var settings = context.Sheet.PageSettings;
        var area = context.PrintArea.Value;
        var bodyColumns = context.VisibleColumns.Where(column => column >= area.First.Column && column <= area.Last.Column).ToArray();
        var bodyRows = context.VisibleRows.Where(row => row >= area.First.Row && row <= area.Last.Row).ToArray();
        var titleColumns = GetIndices(context.VisibleColumns, settings.TitleColumns);
        var titleRows = GetIndices(context.VisibleRows, settings.TitleRows);
        var titleWidth = GetSize(titleColumns, column => context.ColumnLayouts[column].Width);
        var titleHeight = GetSize(titleRows, row => context.RowLayouts[row].Height);
        var titleColumnEnd = titleColumns.Count == 0 ? double.NegativeInfinity
            : context.ColumnLayouts[titleColumns[titleColumns.Count - 1]].X + context.ColumnLayouts[titleColumns[titleColumns.Count - 1]].Width;
        var titleRowEnd = titleRows.Count == 0 ? double.NegativeInfinity
            : context.RowLayouts[titleRows[titleRows.Count - 1]].Y + context.RowLayouts[titleRows[titleRows.Count - 1]].Height;
        var columnEnds = new Dictionary<int, double>();
        var rowEnds = new Dictionary<int, double>();
        if (context.Sheet.RequestedRange is null)
        {
            foreach (var (address, cell) in context.Sheet.Cells)
            {
                if (context.ColumnLayouts.TryGetValue(address.Column + cell.ColumnSpan - 1, out var lastColumn))
                {
                    columnEnds[address.Column] = Math.Max(columnEnds.GetValueOrDefault(address.Column, double.NegativeInfinity), lastColumn.X + lastColumn.Width);
                }

                if (context.RowLayouts.TryGetValue(address.Row + cell.RowSpan - 1, out var lastRow))
                {
                    rowEnds[address.Row] = Math.Max(rowEnds.GetValueOrDefault(address.Row, double.NegativeInfinity), lastRow.Y + lastRow.Height);
                }
            }
        }

        double GetColumnEnd(int column, double end) => Math.Max(end, columnEnds.GetValueOrDefault(column, end));
        double GetRowEnd(int row, double end) => Math.Max(end, rowEnds.GetValueOrDefault(row, end));
        if (bodyColumns.Length == 0 || bodyRows.Length == 0)
        {
            return [new(null, null, [], [], [], [], 0, 0, 0, 0, 1)];
        }

        var scale = PrintScaleResolver.Resolve(
            settings,
            bodyColumns,
            bodyRows,
            column => context.ColumnLayouts[column].X,
            column => context.ColumnLayouts[column].X + context.ColumnLayouts[column].Width,
            GetColumnEnd,
            row => context.RowLayouts[row].Y,
            row => context.RowLayouts[row].Y + context.RowLayouts[row].Height,
            GetRowEnd,
            titleColumnEnd,
            titleWidth,
            titleRowEnd,
            titleHeight);
        var horizontalBands = PageBandBuilder.Create(
            bodyColumns,
            column => context.ColumnLayouts[column].X,
            column => context.ColumnLayouts[column].X + context.ColumnLayouts[column].Width,
            (settings.Width - settings.MarginLeft - settings.MarginRight) / scale,
            GetColumnEnd,
            titleColumnEnd,
            titleWidth,
            PrintScaleResolver.UsesFitMode(settings) ? null : settings.ManualColumnBreaks);
        var verticalBands = PageBandBuilder.Create(
            bodyRows,
            row => context.RowLayouts[row].Y,
            row => context.RowLayouts[row].Y + context.RowLayouts[row].Height,
            (settings.Height - settings.MarginTop - settings.MarginBottom) / scale,
            GetRowEnd,
            titleRowEnd,
            titleHeight,
            PrintScaleResolver.UsesFitMode(settings) ? null : settings.ManualRowBreaks);

        var bandPairs = settings.PageOrder == PrintPageOrder.DownThenOver
            ? horizontalBands.SelectMany((horizontal, horizontalIndex) => verticalBands.Select(
                (vertical, verticalIndex) => (Horizontal: horizontal, HorizontalIndex: horizontalIndex,
                    Vertical: vertical, VerticalIndex: verticalIndex)))
            : verticalBands.SelectMany((vertical, verticalIndex) => horizontalBands.Select(
                (horizontal, horizontalIndex) => (Horizontal: horizontal, HorizontalIndex: horizontalIndex,
                    Vertical: vertical, VerticalIndex: verticalIndex)));
        return bandPairs.Select(pair => new PaginationPagePlan(
            pair.Horizontal, pair.Vertical, bodyColumns, bodyRows, titleColumns, titleRows, titleColumnEnd, titleRowEnd, titleWidth, titleHeight, scale)).ToArray();
    }

    /// <summary>ページ番号を解決したヘッダーおよびフッターの配置を作成します。</summary>
    /// <param name="sheet">ヘッダーおよびフッター設定を持つシートです。</param>
    /// <param name="pageNumber">対象ページ番号です。</param>
    /// <param name="pageCount">シートの総ページ数です。</param>
    /// <param name="timestamp">事前確認と本描画で共有する時刻です。</param>
    /// <returns>ページへ配置するヘッダーおよびフッター文字列です。</returns>
    internal static IReadOnlyList<RenderText> GetHeaderFooterTexts(
        ReportSheet sheet,
        int pageNumber,
        int pageCount,
        DateTime? timestamp = null) => HeaderFooterLayout.Create(sheet, pageNumber, pageCount, timestamp);

    private static IReadOnlyList<int> GetIndices(IReadOnlyList<int> visibleIndices, IndexRange? range) =>
        range is not { } value ? [] : visibleIndices.Where(index => index >= value.First && index <= value.Last).ToArray();

    private static double GetSize(IReadOnlyList<int> indices, Func<int, double> getSize) => indices.Sum(getSize);
}
