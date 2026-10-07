using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>
/// シートにレイアウト工程を順番に適用し、ページ単位の描画データを生成します。
/// </summary>
public sealed class ReportLayoutEngine
{
    private readonly IReadOnlyList<IReportLayoutPass> passes;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReportLayoutEngine"/> class. 文字列の寸法計測に使用する実装を指定して、レイアウトエンジンを初期化します。
    /// </summary>
    /// <param name="textMeasurer">セル文字列の描画幅と高さを計測する実装です。</param>
    public ReportLayoutEngine(ITextMeasurer textMeasurer)
    {
        passes =
        [
            new NormalizePass(), new ResolvePrintAreaPass(), new HiddenRowColumnPass(),
            new ColumnLayoutPass(), new RowLayoutPass(), new ExplicitRangeGeometryPass(), new TextMeasurePass(),
            new CellBoundsPass(), new PaginationPass()
        ];
        TextMeasurer = textMeasurer;
    }

    /// <summary>
    /// Gets the text measurer. セル文字列の描画寸法を求める計測実装を取得します。
    /// </summary>
    public ITextMeasurer TextMeasurer { get; }

    /// <summary>
    /// シートの印刷範囲、行列サイズ、文字寸法、およびページ設定を解決し、描画対象ページへ変換します。
    /// </summary>
    /// <param name="sheet">ページへ配置するセル、画像、図形、および印刷設定を持つシートです。</param>
    /// <returns>ページごとのセル、画像、図形、およびヘッダー・フッターの配置を保持するレンダリング文書を返します。</returns>
    public RenderDocument Layout(ReportSheet sheet)
    {
        if (sheet.PrintAreas.Count > 1)
        {
            var pages = new List<RenderPage>();
            var geometry = new SheetGeometry(sheet);
            foreach (var area in sheet.PrintAreas)
            {
                var areaDocument = LayoutSingleArea(sheet with { PrintArea = area, PrintAreas = [] }, geometry);
                var offset = pages.Count;
                pages.AddRange(areaDocument.Pages.Select(page => page with { Number = offset + page.Number }));
            }

            return new(pages.Select(page => page with
            {
                HeaderFooterTexts = PaginationPass.GetHeaderFooterTexts(sheet, page.Number, pages.Count),
            }).ToArray());
        }

        return LayoutSingleArea(sheet);
    }

    /// <summary>印刷範囲やページ設定を適用せず、使用範囲を単一キャンバスへ配置します。</summary>
    /// <param name="sheet">単一キャンバスへ配置するシートです。</param>
    /// <returns>配置済みの文書とキャンバス寸法を返します。</returns>
    public ContinuousRenderDocument LayoutContinuous(ReportSheet sheet)
    {
        var context = new ReportLayoutContext(sheet, TextMeasurer);
        new NormalizePass().Execute(context);
        new ResolvePrintAreaPass { IgnoreExplicitPrintArea = sheet.RequestedRange is null }.Execute(context);
        new HiddenRowColumnPass { IncludePrintTitles = false }.Execute(context);
        new ColumnLayoutPass().Execute(context);
        new RowLayoutPass().Execute(context);
        new ExplicitRangeGeometryPass().Execute(context);
        new TextMeasurePass().Execute(context);
        new CellBoundsPass().Execute(context);
        new ContinuousLayoutPass().Execute(context);
        var document = context.RenderDocument ?? new RenderDocument([]);
        var page = document.Pages.Count == 0 ? null : document.Pages[0];
        if (page is null)
        {
            return new(document, 1, 1);
        }

        if (sheet.RequestedRange is { } requested)
        {
            var clip = RectangleGeometry.Bounds(context.Geometry, requested);
            var source = new PageSourceRegion(clip, new(0, 0, clip.Width, clip.Height), 1, false) { Cells = requested };
            ReportRect Move(ReportRect rect) => rect with { X = rect.X - clip.X, Y = rect.Y - clip.Y };
            var mappedClip = Move(clip);
            page = page with
            {
                Cells = page.Cells.Where(cell => RectangleGeometry.Intersect(cell.Bounds, clip) is not null)
                    .Select(cell => cell with
                    {
                        Bounds = Move(cell.Bounds),
                        ContentBounds = Move(cell.ContentBounds),
                        ClipBounds = mappedClip,
                        MergedBorders = cell.MergedBorders?.Select(border => border with { Bounds = Move(border.Bounds) }).ToArray(),
                    }).ToArray(),
                Images = page.Images?.Where(image => RectangleGeometry.Intersect(ObjectGeometry.GetVisualBounds(image.Bounds, image.Rotation), clip) is not null)
                    .Select(image => image with { Bounds = Move(image.Bounds), ClipBounds = mappedClip }).ToArray(),
                Shapes = page.Shapes?.Where(shape => RectangleGeometry.Intersect(ObjectGeometry.GetShapeVisualBounds(shape.Bounds, shape.Shape), clip) is not null)
                    .Select(shape => shape with { Bounds = Move(shape.Bounds), ClipBounds = mappedClip }).ToArray(),
                SourceRegions = [source],
            };
            return new(new([page]), clip.Width > 0 && clip.Height > 0 ? clip.Width : 1, clip.Width > 0 && clip.Height > 0 ? clip.Height : 1);
        }

        var visual = page.Cells.SelectMany(cell => new[] { cell.Bounds }.Concat(cell.MergedBorders?.Select(border => border.Bounds) ?? []))
            .Concat((page.Images ?? []).Select(image => ObjectGeometry.GetVisualBounds(image.Bounds, image.Rotation)))
            .Concat((page.Shapes ?? []).Select(shape => ObjectGeometry.GetVisualBounds(shape.Bounds, shape.Shape.Rotation)))
            .ToArray();

        // Rotated objects may extend past the origin; translate every element equally to keep them on the canvas.
        var shiftX = visual.Length == 0 ? 0 : Math.Max(0, -visual.Min(bound => bound.X));
        var shiftY = visual.Length == 0 ? 0 : Math.Max(0, -visual.Min(bound => bound.Y));
        if (shiftX > 0 || shiftY > 0)
        {
            ReportRect Move(ReportRect rect) => rect with { X = rect.X + shiftX, Y = rect.Y + shiftY };
            page = page with
            {
                Cells = page.Cells.Select(cell => cell with
                {
                    Bounds = Move(cell.Bounds),
                    ContentBounds = Move(cell.ContentBounds),
                    MergedBorders = cell.MergedBorders?.Select(border => border with { Bounds = Move(border.Bounds) }).ToArray(),
                }).ToArray(),
                Images = page.Images?.Select(image => image with { Bounds = Move(image.Bounds) }).ToArray(),
                Shapes = page.Shapes?.Select(shape => shape with { Bounds = Move(shape.Bounds) }).ToArray(),
            };
            document = new RenderDocument([page]);
            visual = visual.Select(Move).ToArray();
        }

        var width = visual.Length == 0 ? 1 : Math.Max(1, visual.Max(bound => bound.X + bound.Width));
        var height = visual.Length == 0 ? 1 : Math.Max(1, visual.Max(bound => bound.Y + bound.Height));
        return new(document, width, height);
    }

    private RenderDocument LayoutSingleArea(ReportSheet sheet, SheetGeometry? geometry = null)
    {
        var context = geometry is null
            ? new ReportLayoutContext(sheet, TextMeasurer)
            : new ReportLayoutContext(sheet, TextMeasurer, geometry);
        foreach (var pass in passes)
        {
            ExcelRenderer.Rendering.ConversionMetrics.Measure(pass.GetType().Name, () =>
            {
                pass.Execute(context);
                return true;
            });
        }

        return context.RenderDocument ?? new RenderDocument([]);
    }
}
