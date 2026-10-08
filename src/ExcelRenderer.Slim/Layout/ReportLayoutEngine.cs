using ExcelRenderer.Slim.Abstractions;
using ExcelRenderer.Slim.Model;

namespace ExcelRenderer.Slim.Layout;

/// <summary>
/// シートにレイアウト工程を順番に適用し、ページ単位の描画データを生成します。
/// </summary>
internal sealed class ReportLayoutEngine
{
    private readonly IReadOnlyList<IReportLayoutPass> geometryPasses;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReportLayoutEngine"/> class. 文字列の寸法計測に使用する実装を指定して、レイアウトエンジンを初期化します。
    /// </summary>
    /// <param name="textMeasurer">セル文字列の描画幅と高さを計測する実装です。</param>
    public ReportLayoutEngine(ITextMeasurer textMeasurer)
    {
        geometryPasses =
        [
            new NormalizePass(),
            new ResolvePrintAreaPass(),
            new HiddenRowColumnPass(),
            new ColumnLayoutPass(),
            new RowLayoutPass(),
        ];
        TextMeasurer = textMeasurer;
    }

    /// <summary>
    /// Gets the text measurer. セル文字列の描画寸法を求める計測実装を取得します。
    /// </summary>
    public ITextMeasurer TextMeasurer { get; }

    /// <summary>Prepares row, column, and print-area geometry without measuring cell text, then plans page bands.</summary>
    /// <param name="sheet">The sheet used by this operation.</param>
    /// <returns>The planned or generated result.</returns>
    internal IReadOnlyList<SheetLayoutPlan> Plan(ReportSheet sheet)
    {
        var geometry = new SheetGeometry(sheet);
        var areas = sheet.PrintAreas.Count > 1 ? sheet.PrintAreas.Select(area => sheet with { PrintArea = area, PrintAreas = [] }) : [sheet];
        var plans = areas.Select(area =>
        {
            var context = new ReportLayoutContext(area, TextMeasurer, geometry);
            foreach (var pass in geometryPasses)
            {
                pass.Execute(context);
            }

            return new SheetLayoutPlan(context);
        }).ToArray();
        if (plans.All(plan => plan.Pages.Count == 0))
        {
            plans[0].EnsureEmptyPage();
        }

        return plans;
    }
}
