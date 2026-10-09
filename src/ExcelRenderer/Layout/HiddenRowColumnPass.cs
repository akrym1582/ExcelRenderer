using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>
/// 印刷範囲と印刷タイトルから、非表示設定を除いた描画対象の行および列を抽出します。
/// </summary>
public sealed class HiddenRowColumnPass : IReportLayoutPass
{
    /// <summary>Gets a value indicating whether print titles are included in the visible range.</summary>
    public bool IncludePrintTitles { get; init; } = true;

    /// <summary>
    /// 印刷範囲と印刷タイトルの行列から非表示項目を除外し、描画対象の行番号と列番号を確定します。
    /// </summary>
    /// <param name="context">入力シート、計測機能、および各工程の計算結果を保持するレイアウトコンテキストです。</param>
    public void Execute(ReportLayoutContext context)
    {
        var core = CoreIntegration.CoreLayoutContextAdapter.CreateGeometry(context);
        new Core.Layout.HiddenRowColumnPass { IncludePrintTitles = IncludePrintTitles }.Execute(core);
        context.VisibleColumns = core.VisibleColumns;
        context.VisibleRows = core.VisibleRows;
    }
}
