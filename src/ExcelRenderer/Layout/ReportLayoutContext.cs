using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>
/// 帳票レイアウト処理で共有するシート情報と計算結果を保持します。
/// </summary>
public sealed class ReportLayoutContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ReportLayoutContext"/> class. 入力シートと文字計測実装を指定して、レイアウト工程間で共有するコンテキストを初期化します。
    /// </summary>
    /// <param name="sheet">レイアウト対象のシートです。</param>
    /// <param name="textMeasurer">セル文字列の描画寸法を求める計測実装です。</param>
    public ReportLayoutContext(ReportSheet sheet, ITextMeasurer textMeasurer)
    {
        Sheet = sheet;
        TextMeasurer = textMeasurer;
    }

    /// <summary>
    /// Gets the source worksheet. レイアウト対象のシートを取得します。
    /// </summary>
    public ReportSheet Sheet { get; }

    /// <summary>
    /// Gets the text measurer. セル文字列の描画寸法を求める計測実装を取得します。
    /// </summary>
    public ITextMeasurer TextMeasurer { get; }

    /// <summary>
    /// Gets or sets the resolved print area. シートから解決した印刷対象のセル範囲を取得または設定します。
    /// </summary>
    public CellRange? PrintArea { get; set; }

    /// <summary>
    /// Gets or sets the visible column indices. 印刷範囲と印刷タイトルに含まれる、非表示列を除いた列番号を取得または設定します。
    /// </summary>
    public IReadOnlyList<int> VisibleColumns { get; set; } = [];

    /// <summary>
    /// Gets or sets the visible row indices. 印刷範囲と印刷タイトルに含まれる、非表示行を除いた行番号を取得または設定します。
    /// </summary>
    public IReadOnlyList<int> VisibleRows { get; set; } = [];

    /// <summary>
    /// Gets the calculated column layouts. 列番号ごとに算出した水平位置と列幅を取得します。
    /// </summary>
    public Dictionary<int, ColumnLayout> ColumnLayouts { get; } = [];

    /// <summary>
    /// Gets the calculated row layouts. 行番号ごとに算出した垂直位置と行高を取得します。
    /// </summary>
    public Dictionary<int, RowLayout> RowLayouts { get; } = [];

    /// <summary>
    /// Gets the measured text sizes. セルアドレスごとに計測した文字列の幅と高さを取得します。
    /// </summary>
    public Dictionary<CellAddress, TextSize> TextSizes { get; } = [];

    /// <summary>
    /// Gets the calculated cell layouts. セルアドレスごとに算出した配置矩形と文字寸法を取得します。
    /// </summary>
    public Dictionary<CellAddress, CellLayout> CellLayouts { get; } = [];

    /// <summary>
    /// Gets or sets the rendered document. 全レイアウト工程から生成されたページ集合を取得または設定します。
    /// </summary>
    public RenderDocument? RenderDocument { get; set; }
}
