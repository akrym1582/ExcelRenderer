using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>Preserves the main planning facade while Core owns candidates and page materialization.</summary>
internal sealed class SheetLayoutPlan
{
    private readonly Core.Layout.SheetLayoutPlan inner;
    private readonly ReportSheet sheet;
    private readonly ITextMeasurer measurer;
    private readonly Core.Abstractions.ITextMeasurer coreMeasurer;

    /// <summary>Initializes a new instance of the <see cref="SheetLayoutPlan"/> class.</summary>
    /// <param name="context">The independent public layout context.</param>
    internal SheetLayoutPlan(ReportLayoutContext context)
        : this(new Core.Layout.SheetLayoutPlan(CoreIntegration.CoreLayoutContextAdapter.CreateGeometry(context)), context.Sheet, context.TextMeasurer, CoreIntegration.CoreTextMeasurerAdapter.Create(context.TextMeasurer))
    {
    }

    /// <summary>Initializes a new instance of the <see cref="SheetLayoutPlan"/> class with the common plan.</summary>
    /// <param name="inner">The common lightweight plan.</param>
    /// <param name="sheet">The original public header/footer metadata.</param>
    /// <param name="measurer">The public measurement implementation.</param>
    /// <param name="coreMeasurer">The conversion-local measurement adapter.</param>
    internal SheetLayoutPlan(Core.Layout.SheetLayoutPlan inner, ReportSheet sheet, ITextMeasurer measurer, Core.Abstractions.ITextMeasurer coreMeasurer)
    {
        this.inner = inner;
        this.sheet = sheet;
        this.measurer = measurer;
        this.coreMeasurer = coreMeasurer;
        Pages = inner.Pages.Select(CoreIntegration.CorePageAdapter.ToPublic).ToArray();
    }

    /// <summary>Gets the planned bands without render payloads.</summary>
    internal IReadOnlyList<PaginationPagePlan> Pages { get; }

    /// <summary>Builds one page using the common candidate, measurement and placement path.</summary>
    /// <param name="index">The source page index.</param>
    /// <param name="number">The page number.</param>
    /// <param name="count">The product page count.</param>
    /// <param name="measurer">The requested public measurer.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The durable public page.</returns>
    internal RenderPage Build(int index, int number, int count, ITextMeasurer measurer, CancellationToken cancellationToken = default) =>
        CoreIntegration.CorePageAdapter.ToPublic(BuildCore(index, number, count, measurer, cancellationToken)) with
        {
            HeaderFooterTexts = PaginationPass.GetHeaderFooterTexts(sheet, number, count, Rendering.RenderResourceSession.Current?.HeaderFooterTimestamp),
        };

    /// <summary>Builds the direct neutral page for internal output without a public page round trip.</summary>
    /// <param name="index">The source page index.</param>
    /// <param name="number">The page number.</param>
    /// <param name="count">The sheet page count.</param>
    /// <param name="measurer">The product measurer.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The neutral page with borrowed full runs and objects.</returns>
    internal Core.Layout.RenderPage BuildCore(int index, int number, int count, ITextMeasurer measurer, CancellationToken cancellationToken) =>
        inner.Build(index, number, count, ReferenceEquals(measurer, this.measurer) ? coreMeasurer : CoreIntegration.CoreTextMeasurerAdapter.Create(measurer), cancellationToken);
}
