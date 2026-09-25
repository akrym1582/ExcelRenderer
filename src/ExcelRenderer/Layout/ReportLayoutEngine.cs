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
}
