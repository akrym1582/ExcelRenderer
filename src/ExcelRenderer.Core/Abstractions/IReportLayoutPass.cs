using ExcelRenderer.Core.Drawing;
using ExcelRenderer.Core.Layout;
using ExcelRenderer.Core.Model;

namespace ExcelRenderer.Core.Abstractions;

/// <summary>
/// 帳票レイアウトの計算工程を構成する処理単位を定義します。
/// </summary>
internal interface IReportLayoutPass
{
    /// <summary>
    /// 共有コンテキストを読み取り、この工程で求めたレイアウト情報を同じコンテキストへ反映します。
    /// </summary>
    /// <param name="context">入力シート、計測機能、および各工程の計算結果を保持するレイアウトコンテキストです。</param>
    void Execute(ReportLayoutContext context);
}
