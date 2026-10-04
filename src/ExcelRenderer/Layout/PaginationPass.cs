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
        if (context.PrintArea is not { } ||
            (context.Sheet.RequestedRange is null && context.Sheet.Hyperlinks.Count == 0 && context.CellLayouts.Count == 0 && (context.Sheet.Images?.Count ?? 0) == 0 && (context.Sheet.Shapes?.Count ?? 0) == 0))
        {
            var headerFooterTexts = HeaderFooterLayout.Create(context.Sheet, 1, 1);
            context.RenderDocument = new(headerFooterTexts.Count == 0
                ? []
                : [new RenderPage(1, [], HeaderFooterTexts: headerFooterTexts)]);
            return;
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
        double GetColumnEnd(int column, double end) => context.Sheet.RequestedRange is not null ? end : context.Sheet.Cells
            .Where(cell => cell.Key.Column == column)
            .Select(cell => cell.Key.Column + cell.Value.ColumnSpan - 1)
            .Where(context.ColumnLayouts.ContainsKey)
            .Select(last => context.ColumnLayouts[last].X + context.ColumnLayouts[last].Width)
            .Append(end).Max();
        double GetRowEnd(int row, double end) => context.Sheet.RequestedRange is not null ? end : context.Sheet.Cells
            .Where(cell => cell.Key.Row == row)
            .Select(cell => cell.Key.Row + cell.Value.RowSpan - 1)
            .Where(context.RowLayouts.ContainsKey)
            .Select(last => context.RowLayouts[last].Y + context.RowLayouts[last].Height)
            .Append(end).Max();
        if (bodyColumns.Length == 0 || bodyRows.Length == 0)
        {
            context.RenderDocument = new([new RenderPage(1, [], HeaderFooterTexts: HeaderFooterLayout.Create(context.Sheet, 1, 1))]);
            return;
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

        var pageCount = horizontalBands.Count * verticalBands.Count;
        var bandPairs = settings.PageOrder == PrintPageOrder.DownThenOver
            ? horizontalBands.SelectMany((horizontal, horizontalIndex) => verticalBands.Select(
                (vertical, verticalIndex) => (Horizontal: horizontal, HorizontalIndex: horizontalIndex,
                    Vertical: vertical, VerticalIndex: verticalIndex)))
            : verticalBands.SelectMany((vertical, verticalIndex) => horizontalBands.Select(
                (horizontal, horizontalIndex) => (Horizontal: horizontal, HorizontalIndex: horizontalIndex,
                    Vertical: vertical, VerticalIndex: verticalIndex)));
        var pageBuilder = new RenderPageBuilder(
            context,
            bodyColumns,
            bodyRows,
            titleColumns,
            titleRows,
            titleColumnEnd,
            titleRowEnd,
            titleWidth,
            titleHeight,
            scale);
        var pages = bandPairs.Select((pair, pageIndex) => pageBuilder.Build(
            pageIndex + 1,
            pair.Horizontal,
            pair.Vertical)).ToArray();
        context.RenderDocument = new(pages.Select(page => page with
        {
            HeaderFooterTexts = HeaderFooterLayout.Create(context.Sheet, page.Number, pageCount),
        }).ToArray());
    }

    /// <summary>ページ番号を解決したヘッダーおよびフッターの配置を作成します。</summary>
    /// <param name="sheet">ヘッダーおよびフッター設定を持つシートです。</param>
    /// <param name="pageNumber">対象ページ番号です。</param>
    /// <param name="pageCount">シートの総ページ数です。</param>
    /// <returns>ページへ配置するヘッダーおよびフッター文字列です。</returns>
    internal static IReadOnlyList<RenderText> GetHeaderFooterTexts(
        ReportSheet sheet,
        int pageNumber,
        int pageCount) => HeaderFooterLayout.Create(sheet, pageNumber, pageCount);

    private static IReadOnlyList<int> GetIndices(IReadOnlyList<int> visibleIndices, IndexRange? range) =>
        range is not { } value ? [] : visibleIndices.Where(index => index >= value.First && index <= value.Last).ToArray();

    private static double GetSize(IReadOnlyList<int> indices, Func<int, double> getSize) => indices.Sum(getSize);
}
