using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>
/// 明示された印刷範囲を採用し、未指定の場合はセル、画像、および図形から使用範囲を求めます。
/// </summary>
public sealed class ResolvePrintAreaPass : IReportLayoutPass
{
    /// <summary>Gets a value indicating whether an explicit print area is ignored.</summary>
    public bool IgnoreExplicitPrintArea { get; init; }

    /// <summary>
    /// 明示された印刷範囲を採用し、未指定の場合は内容が存在するセル、画像、および図形を包含する範囲を設定します。
    /// </summary>
    /// <param name="context">入力シート、計測機能、および各工程の計算結果を保持するレイアウトコンテキストです。</param>
    public void Execute(ReportLayoutContext context)
    {
        context.PrintArea = !IgnoreExplicitPrintArea ? context.Sheet.PrintArea ?? GetUsedRange(context.Sheet) : GetUsedRange(context.Sheet);
    }

    /// <summary>Gets the get used range.</summary>
    /// <param name="sheet">The sheet used by this operation.</param>
    /// <returns>The planned or generated result.</returns>
    internal static CellRange? GetUsedRange(ReportSheet sheet)
    {
        var core = new Core.Layout.ReportLayoutContext(CoreIntegration.CoreModelAdapter.ToCore(sheet), new Core.Layout.RangeOnlyMeasurer()) { Policy = new CoreIntegration.FullLayoutPolicy() };
        var range = core.Policy.ResolveUsedRange(core);
        return range is { } value ? CoreIntegration.CoreModelAdapter.ToPublic(value) : null;
    }
}
