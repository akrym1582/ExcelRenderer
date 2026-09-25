using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>
/// 入力元ごとの差異を後続のレイアウト工程で扱える形式へ正規化する境界を提供します。
/// </summary>
public sealed class NormalizePass : IReportLayoutPass
{
    /// <summary>
    /// 入力シートを後続工程が前提とする表現へ正規化します。
    /// </summary>
    /// <param name="context">入力シート、計測機能、および各工程の計算結果を保持するレイアウトコンテキストです。</param>
    public void Execute(ReportLayoutContext context)
    {
        // ExcelReader represents merged ranges on their top-left cell; this pass
        // remains the explicit normalization boundary for additional input providers.
    }
}
