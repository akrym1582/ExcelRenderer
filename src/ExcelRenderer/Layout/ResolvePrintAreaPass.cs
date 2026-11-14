using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>
/// 印刷範囲を解決し、未指定の場合は A1 から内容の終端までを保持します。
/// </summary>
public sealed class ResolvePrintAreaPass : IReportLayoutPass
{
    /// <summary>Gets a value indicating whether an explicit print area is ignored.</summary>
    public bool IgnoreExplicitPrintArea { get; init; }

    /// <summary>
    /// 列全体・行全体の印刷範囲の未指定軸を使用範囲で制限し、自動印刷では先頭の空白行列を保持します。
    /// </summary>
    /// <param name="context">入力シート、計測機能、および各工程の計算結果を保持するレイアウトコンテキストです。</param>
    public void Execute(ReportLayoutContext context)
    {
        var core = CoreIntegration.CoreLayoutContextAdapter.CreateGeometry(context);
        new Core.Layout.ResolvePrintAreaPass { IgnoreExplicitPrintArea = IgnoreExplicitPrintArea }.Execute(core);
        context.PrintArea = core.PrintArea is { } area ? CoreIntegration.CoreModelAdapter.ToPublic(area) : null;
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
