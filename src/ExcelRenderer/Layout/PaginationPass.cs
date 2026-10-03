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
            (context.CellLayouts.Count == 0 && (context.Sheet.Images?.Count ?? 0) == 0 && (context.Sheet.Shapes?.Count ?? 0) == 0))
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
        double GetColumnEnd(int column, double end) => context.Sheet.Cells
            .Where(cell => cell.Key.Column == column)
            .Select(cell => cell.Key.Column + cell.Value.ColumnSpan - 1)
            .Where(context.ColumnLayouts.ContainsKey)
            .Select(last => context.ColumnLayouts[last].X + context.ColumnLayouts[last].Width)
            .Append(end).Max();
        double GetRowEnd(int row, double end) => context.Sheet.Cells
            .Where(cell => cell.Key.Row == row)
            .Select(cell => cell.Key.Row + cell.Value.RowSpan - 1)
            .Where(context.RowLayouts.ContainsKey)
            .Select(last => context.RowLayouts[last].Y + context.RowLayouts[last].Height)
            .Append(end).Max();
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
        var pages = bandPairs.Select((pair, pageIndex) =>
        {
            var horizontal = pair.Horizontal;
            var vertical = pair.Vertical;
            var repeatColumns = horizontal.Start >= titleColumnEnd - 1e-7;
            var repeatRows = vertical.Start >= titleRowEnd - 1e-7;
            var repeatedWidth = repeatColumns ? titleWidth : 0;
            var repeatedHeight = repeatRows ? titleHeight : 0;
            var horizontalEnd = double.IsFinite(horizontal.End)
                ? horizontal.End
                : context.ColumnLayouts[bodyColumns[bodyColumns.Length - 1]].X +
                    context.ColumnLayouts[bodyColumns[bodyColumns.Length - 1]].Width;
            var verticalEnd = double.IsFinite(vertical.End)
                ? vertical.End
                : context.RowLayouts[bodyRows[bodyRows.Length - 1]].Y +
                    context.RowLayouts[bodyRows[bodyRows.Length - 1]].Height;
            var placement = new PagePlacement(
                settings,
                horizontal,
                vertical,
                horizontalEnd,
                verticalEnd,
                repeatedWidth,
                repeatedHeight,
                scale);
            var cells = context.CellLayouts.Values
                .Where(layout => ((layout.Bounds.X >= horizontal.Start && layout.Bounds.X < horizontal.End) ||
                        (repeatColumns && titleColumns.Contains(layout.Address.Column))) &&
                    ((layout.Bounds.Y >= vertical.Start && layout.Bounds.Y < vertical.End) ||
                        (repeatRows && titleRows.Contains(layout.Address.Row))))
                .Select(layout =>
                {
                    var isTitleColumn = titleColumns.Contains(layout.Address.Column);
                    var isTitleRow = titleRows.Contains(layout.Address.Row);
                    double PageX(double x) => placement.MapCellX(
                        x,
                        repeatColumns,
                        isTitleColumn,
                        titleColumns.Count == 0 ? 0 : context.ColumnLayouts[titleColumns[0]].X);
                    double PageY(double y) => placement.MapCellY(
                        y,
                        repeatRows,
                        isTitleRow,
                        titleRows.Count == 0 ? 0 : context.RowLayouts[titleRows[0]].Y);
                    var cellBounds = new ReportRect(
                        PageX(layout.Bounds.X),
                        PageY(layout.Bounds.Y),
                        layout.Bounds.Width * scale,
                        layout.Bounds.Height * scale);
                    return new RenderCell(ScaleCell(context.Sheet.Cells[layout.Address], scale), cellBounds)
                    {
                        ContentBounds = new(
                            cellBounds.X + ((layout.ContentBounds.X - layout.Bounds.X) * scale),
                            cellBounds.Y + ((layout.ContentBounds.Y - layout.Bounds.Y) * scale),
                            layout.ContentBounds.Width * scale,
                            layout.ContentBounds.Height * scale),
                        TextLayout = context.TextLayouts.TryGetValue(layout.Address, out var textLayout)
                            ? TextLayoutTransform.Scale(textLayout, scale)
                            : null,
                        MergedBorders = layout.MergedBorders?.Select(border => new RenderBorder(
                            new(
                                PageX(border.Bounds.X),
                                PageY(border.Bounds.Y),
                                border.Bounds.Width * scale,
                                border.Bounds.Height * scale),
                            ScaleBorder(border.Border, scale))).ToArray(),
                    };
                })
                .ToArray();
            var images = (context.Sheet.Images ?? [])
                .Where(image => TryGetObjectBounds(
                    context,
                    image.Anchor,
                    image.OffsetX,
                    image.OffsetY,
                    image.Width,
                    image.Height,
                    image.DrawingAnchor,
                    out var bounds) && Intersects(ObjectGeometry.GetVisualBounds(bounds, image.Rotation), horizontal, vertical))
                .Select(image =>
                {
                    DrawingAnchorResolver.TryResolve(
                        context,
                        image.Anchor,
                        image.OffsetX,
                        image.OffsetY,
                        image.Width,
                        image.Height,
                        image.DrawingAnchor,
                        out var sourceBounds);
                    return new RenderImage(
                        placement.MapBodyObjectBounds(sourceBounds),
                        image.ImageBytes,
                        image.ZIndex)
                    {
                        Crop = image.Crop,
                        Rotation = image.Rotation,
                        FlipHorizontal = image.FlipHorizontal,
                        FlipVertical = image.FlipVertical,
                        ClipBounds = placement.BodyClip,
                    };
                })
                .ToArray();
            var shapes = (context.Sheet.Shapes ?? [])
                .Where(shape => TryGetObjectBounds(
                    context,
                    shape.Anchor,
                    shape.OffsetX,
                    shape.OffsetY,
                    shape.Width,
                    shape.Height,
                    shape.DrawingAnchor,
                    out var bounds) && Intersects(ObjectGeometry.GetVisualBounds(bounds, shape.Rotation), horizontal, vertical))
                .Select(shape =>
                {
                    DrawingAnchorResolver.TryResolve(
                        context,
                        shape.Anchor,
                        shape.OffsetX,
                        shape.OffsetY,
                        shape.Width,
                        shape.Height,
                        shape.DrawingAnchor,
                        out var sourceBounds);
                    return new RenderShape(
                        placement.MapBodyObjectBounds(sourceBounds),
                        ScaleShape(shape, scale))
                    {
                        ClipBounds = placement.BodyClip,
                    };
                }).ToArray();
            return new RenderPage(pageIndex + 1, cells, images, Shapes: shapes);
        }).ToArray();
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

    private static BorderStyle ScaleBorder(BorderStyle border, double scale)
    {
        BorderSide? Side(BorderSide? side) => side is null ? null : side with { Width = side.Width * scale };
        return new(Side(border.Left), Side(border.Top), Side(border.Right), Side(border.Bottom));
    }

    private static ReportCell ScaleCell(ReportCell cell, double scale)
    {
        return cell with
        {
            Style = cell.Style with
            {
                Font = cell.Style.Font with { Size = cell.Style.Font.Size * scale },
                Border = cell.Style.Border is { } border ? ScaleBorder(border, scale) : null,
            },
        };
    }

    private static ReportShape ScaleShape(ReportShape shape, double scale) => shape with
    {
        Style = shape.Style with { LineWidth = shape.Style.LineWidth * scale },
        Text = shape.Text is not { } text ? null : text with
        {
            Font = text.Font with { Size = text.Font.Size * scale },
            MarginLeft = text.MarginLeft * scale,
            MarginTop = text.MarginTop * scale,
            MarginRight = text.MarginRight * scale,
            MarginBottom = text.MarginBottom * scale,
        },
    };

    private static bool TryGetObjectBounds(
        ReportLayoutContext context,
        CellAddress anchor,
        double offsetX,
        double offsetY,
        double width,
        double height,
        DrawingAnchor? drawingAnchor,
        out ReportRect bounds)
    {
        return DrawingAnchorResolver.TryResolve(
            context, anchor, offsetX, offsetY, width, height, drawingAnchor, out bounds);
    }

    private static bool Intersects(ReportRect bounds, PageBand horizontal, PageBand vertical) =>
        bounds.X < horizontal.End && bounds.X + bounds.Width > horizontal.Start &&
        bounds.Y < vertical.End && bounds.Y + bounds.Height > vertical.Start;

    private static IReadOnlyList<int> GetIndices(IReadOnlyList<int> visibleIndices, IndexRange? range) =>
        range is not { } value ? [] : visibleIndices.Where(index => index >= value.First && index <= value.Last).ToArray();

    private static double GetSize(IReadOnlyList<int> indices, Func<int, double> getSize) => indices.Sum(getSize);
}
