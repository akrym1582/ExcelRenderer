using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>
/// 印刷範囲を用紙サイズに分割し、拡大縮小、余白、印刷タイトル、およびヘッダー・フッターを反映したページを生成します。
/// </summary>
public sealed class PaginationPass : IReportLayoutPass
{
    /// <summary>
    /// 印刷可能領域に合わせてセル、画像、および図形をページへ分割し、印刷タイトルとヘッダー・フッターを配置します。
    /// </summary>
    /// <param name="context">入力シート、計測機能、および各工程の計算結果を保持するレイアウトコンテキストです。</param>
    public void Execute(ReportLayoutContext context)
    {
        var plans = Plan(context);
        context.RenderDocument = new(plans.Select((plan, index) => Materialize(context, plan, index + 1, plans.Count)).ToArray());
    }

    /// <summary>Builds a single planned page and resolves its header and footer.</summary>
    /// <param name="context">The context used by this operation.</param>
    /// <param name="plan">The plan used by this operation.</param>
    /// <param name="number">The number used by this operation.</param>
    /// <param name="count">The count used by this operation.</param>
    /// <returns>The planned or generated result.</returns>
    internal static RenderPage Materialize(ReportLayoutContext context, PaginationPagePlan plan, int number, int count)
    {
        var page = plan.Horizontal is null || plan.Vertical is null
            ? new RenderPage(number, [])
            : new RenderPageBuilder(context, plan.BodyColumns, plan.BodyRows, plan.TitleColumns, plan.TitleRows, plan.TitleColumnEnd, plan.TitleRowEnd, plan.TitleWidth, plan.TitleHeight, plan.Scale)
                .Build(number, plan.Horizontal.Value, plan.Vertical.Value);
        return page with { HeaderFooterTexts = HeaderFooterLayout.Create(context.Sheet, number, count) };
    }

    /// <summary>Plans page bands without measuring cell text.</summary>
    /// <param name="context">The context used by this operation.</param>
    /// <returns>The planned or generated result.</returns>
    internal static IReadOnlyList<PaginationPagePlan> Plan(ReportLayoutContext context)
    {
        return Core.Layout.PaginationPass.Plan(CoreIntegration.CoreLayoutContextAdapter.CreateGeometry(context)).Select(CoreIntegration.CorePageAdapter.ToPublic).ToArray();
    }

    /// <summary>ページ番号を解決したヘッダーおよびフッターの配置を作成します。</summary>
    /// <param name="sheet">ヘッダーおよびフッター設定を持つシートです。</param>
    /// <param name="pageNumber">対象ページ番号です。</param>
    /// <param name="pageCount">シートの総ページ数です。</param>
    /// <param name="timestamp">事前確認と本描画で共有する時刻です。</param>
    /// <returns>ページへ配置するヘッダーおよびフッター文字列です。</returns>
    internal static IReadOnlyList<RenderText> GetHeaderFooterTexts(
        ReportSheet sheet,
        int pageNumber,
        int pageCount,
        DateTime? timestamp = null) => HeaderFooterLayout.Create(sheet, pageNumber, pageCount, timestamp);
}
