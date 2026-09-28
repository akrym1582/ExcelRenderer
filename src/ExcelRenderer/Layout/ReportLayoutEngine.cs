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
            new ColumnLayoutPass(), new RowLayoutPass(), new TextMeasurePass(),
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
        var context = new ReportLayoutContext(sheet, TextMeasurer);
        foreach (var pass in passes)
        {
            pass.Execute(context);
        }

        return context.RenderDocument ?? new RenderDocument([]);
    }

    /// <summary>印刷範囲やページ設定を適用せず、使用範囲を単一キャンバスへ配置します。</summary>
    /// <param name="sheet">単一キャンバスへ配置するシートです。</param>
    /// <returns>配置済みの文書とキャンバス寸法を返します。</returns>
    public ContinuousRenderDocument LayoutContinuous(ReportSheet sheet)
    {
        var context = new ReportLayoutContext(sheet, TextMeasurer);
        new NormalizePass().Execute(context);
        new ResolvePrintAreaPass { IgnoreExplicitPrintArea = true }.Execute(context);
        new HiddenRowColumnPass { IncludePrintTitles = false }.Execute(context);
        new ColumnLayoutPass().Execute(context);
        new RowLayoutPass().Execute(context);
        new TextMeasurePass().Execute(context);
        new CellBoundsPass().Execute(context);
        new ContinuousLayoutPass().Execute(context);
        var document = context.RenderDocument ?? new RenderDocument([]);
        var bounds = document.Pages.SelectMany(page => page.Cells.Select(cell => cell.Bounds)
            .Concat((page.Images ?? []).Select(image => image.Bounds))
            .Concat((page.Shapes ?? []).Select(shape => shape.Bounds))).ToArray();
        var width = bounds.Length == 0 ? 1 : Math.Max(1, bounds.Max(bound => bound.X + bound.Width));
        var height = bounds.Length == 0 ? 1 : Math.Max(1, bounds.Max(bound => bound.Y + bound.Height));
        return new(document, width, height);
    }
}
