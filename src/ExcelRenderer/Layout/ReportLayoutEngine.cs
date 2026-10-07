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
        var plans = Plan(sheet);
        var count = plans.Sum(plan => plan.Pages.Count);
        var number = 0;
        var pages = new List<RenderPage>();
        foreach (var plan in plans)
        {
            for (var index = 0; index < plan.Pages.Count; index++)
            {
                pages.Add(plan.Build(index, ++number, count, TextMeasurer) with
                {
                    HeaderFooterTexts = PaginationPass.GetHeaderFooterTexts(sheet, number, count),
                });
            }
        }

        return new(pages);
    }

    /// <summary>印刷範囲やページ設定を適用せず、使用範囲を単一キャンバスへ配置します。</summary>
    /// <param name="sheet">単一キャンバスへ配置するシートです。</param>
    /// <returns>配置済みの文書とキャンバス寸法を返します。</returns>
    public ContinuousRenderDocument LayoutContinuous(ReportSheet sheet)
    {
        var plan = new ContinuousLayoutPlan(sheet, TextMeasurer);
        var page = new RenderPage(1, plan.Cells(TextMeasurer, measureText: true).ToArray(), plan.Images, Shapes: plan.Shapes)
        {
            SourceRegions = plan.SourceRegions,
        };
        return new(new([page]), plan.Width, plan.Height);
    }

    /// <summary>Plans page bands without measuring cell text.</summary>
    /// <param name="sheet">The sheet used by this operation.</param>
    /// <returns>The planned or generated result.</returns>
    internal IReadOnlyList<SheetLayoutPlan> Plan(ReportSheet sheet)
    {
        var geometry = new SheetGeometry(sheet);
        var areas = sheet.PrintAreas.Count > 1 ? sheet.PrintAreas.Select(area => sheet with { PrintArea = area, PrintAreas = [] }) : [sheet];
        return areas.Select(area =>
        {
            var context = new ReportLayoutContext(area, TextMeasurer, geometry);
            foreach (var pass in passes.Take(6))
            {
                pass.Execute(context);
            }

            return new SheetLayoutPlan(context);
        }).ToArray();
    }
}
