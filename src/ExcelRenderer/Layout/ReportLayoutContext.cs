using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>
/// 帳票レイアウト処理で共有するシート情報と計算結果を保持します.
/// </summary>
public sealed class ReportLayoutContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ReportLayoutContext"/> class. 必要な設定を使用して新しいインスタンスを初期化します.
    /// </summary>
    /// <param name="sheet">sheet に渡す値です。</param>
    /// <param name="textMeasurer">textMeasurer に渡す値です。</param>
    public ReportLayoutContext(ReportSheet sheet, ITextMeasurer textMeasurer)
    {
        Sheet = sheet;
        TextMeasurer = textMeasurer;
    }

    /// <summary>
    /// Gets the value. 対応する値を取得または設定します.
    /// </summary>
    public ReportSheet Sheet { get; }

    /// <summary>
    /// Gets the value. 対応する値を取得または設定します.
    /// </summary>
    public ITextMeasurer TextMeasurer { get; }

    /// <summary>
    /// Gets or sets the value. 対応する値を取得または設定します.
    /// </summary>
    public CellRange? PrintArea { get; set; }

    /// <summary>
    /// Gets or sets the value. 対応する値を取得または設定します.
    /// </summary>
    public IReadOnlyList<int> VisibleColumns { get; set; } = [];

    /// <summary>
    /// Gets or sets the value. 対応する値を取得または設定します.
    /// </summary>
    public IReadOnlyList<int> VisibleRows { get; set; } = [];

    /// <summary>
    /// Gets the value. 対応する値を取得または設定します.
    /// </summary>
    public Dictionary<int, ColumnLayout> ColumnLayouts { get; } = [];

    /// <summary>
    /// Gets the value. 対応する値を取得または設定します.
    /// </summary>
    public Dictionary<int, RowLayout> RowLayouts { get; } = [];

    /// <summary>
    /// Gets the value. 対応する値を取得または設定します.
    /// </summary>
    public Dictionary<CellAddress, TextSize> TextSizes { get; } = [];

    /// <summary>
    /// Gets the value. 対応する値を取得または設定します.
    /// </summary>
    public Dictionary<CellAddress, CellLayout> CellLayouts { get; } = [];

    /// <summary>
    /// Gets or sets the value. 対応する値を取得または設定します.
    /// </summary>
    public RenderDocument? RenderDocument { get; set; }
}
