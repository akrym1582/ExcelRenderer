using ExcelRenderer.Core.Abstractions;
using ExcelRenderer.Core.Model;

namespace ExcelRenderer.Core.Layout;

/// <summary>
/// シートにレイアウト工程を順番に適用し、ページ単位の描画データを生成します。
/// </summary>
internal sealed class ReportLayoutEngine
{
    private readonly IReadOnlyList<IReportLayoutPass> geometryPasses;
    private readonly CoreLayoutPolicy policy;
    private readonly bool ensureEmptyPage;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReportLayoutEngine"/> class. 文字列の寸法計測に使用する実装を指定して、レイアウトエンジンを初期化します。
    /// </summary>
    /// <param name="textMeasurer">セル文字列の描画幅と高さを計測する実装です。</param>
    public ReportLayoutEngine(ITextMeasurer textMeasurer)
        : this(textMeasurer, new CoreLayoutPolicy(), [new NormalizePass(), new ResolvePrintAreaPass(), new HiddenRowColumnPass(), new ColumnLayoutPass(), new RowLayoutPass()], true)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ReportLayoutEngine"/> class with a snapshotted product pipeline.</summary>
    /// <param name="textMeasurer">The conversion-local text measurer.</param>
    /// <param name="policy">The fixed product policy.</param>
    /// <param name="geometryPasses">The product pass sequence, created outside constructors.</param>
    /// <param name="ensureEmptyPage">Whether the Core converter supplies a sheet-level blank page.</param>
    internal ReportLayoutEngine(ITextMeasurer textMeasurer, CoreLayoutPolicy policy, IEnumerable<IReportLayoutPass> geometryPasses, bool ensureEmptyPage = false)
    {
        TextMeasurer = textMeasurer;
        this.policy = policy;
        this.geometryPasses = geometryPasses.ToArray();
        this.ensureEmptyPage = ensureEmptyPage;
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
        Rendering.ConversionMetrics.Report("coreGeometryPlan", 1);
        var geometry = new SheetGeometry(sheet);
        var areas = sheet.PrintAreas.Count > 1 ? sheet.PrintAreas.Select(area => sheet with { PrintArea = area, PrintAreas = [] }) : [sheet];
        var plans = areas.Select(area =>
        {
            var context = new ReportLayoutContext(area, TextMeasurer, geometry) { Policy = policy };
            foreach (var pass in geometryPasses)
            {
                pass.Execute(context);
            }

            return new SheetLayoutPlan(context);
        }).ToArray();
        if (ensureEmptyPage && plans.All(plan => plan.Pages.Count == 0))
        {
            plans[0].EnsureEmptyPage();
        }

        return plans;
    }
}
