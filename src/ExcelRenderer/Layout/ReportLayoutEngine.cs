using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>
    /// Initializes a new instance of the class. 必要な設定を使って新しいインスタンスを初期化します.
/// </summary>
public sealed class ReportLayoutEngine
{
    private readonly IReadOnlyList<IReportLayoutPass> passes;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReportLayoutEngine"/> class. 必要な設定を使用して新しいインスタンスを初期化します.
    /// </summary>
    /// <param name="textMeasurer">textMeasurer に渡す値です。</param>
    public ReportLayoutEngine(ITextMeasurer textMeasurer)
    {
        passes =
        [
            new NormalizePass(), new ResolvePrintAreaPass(), new HiddenRowColumnPass(),
            new ColumnLayoutPass(), new RowLayoutPass(), new TextMeasurePass(),
            new CellBoundsPass(), new PaginationPass()
        ];
        TextMeasurer = textMeasurer;
    }

    /// <summary>
    /// Gets the value. 対応する値を取得または設定します.
    /// </summary>
    public ITextMeasurer TextMeasurer { get; }

    /// <summary>
    /// Layout を実行します.
    /// </summary>
    /// <param name="sheet">sheet に渡す値です。</param>
    /// <returns>処理によって得られた結果を返します。</returns>
    public RenderDocument Layout(ReportSheet sheet)
    {
        var context = new ReportLayoutContext(sheet, TextMeasurer);
        foreach (var pass in passes)
        {
            pass.Execute(context);
        }

        return context.RenderDocument ?? new RenderDocument([]);
    }
}
