using ExcelRenderer.Drawing;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;

namespace ExcelRenderer.Abstractions;

/// <summary>
/// 帳票レイアウトの計算工程を構成する処理単位を定義します。
/// </summary>
public interface IReportLayoutPass
{
    /// <summary>
    /// 共有コンテキストを読み取り、この工程で求めたレイアウト情報を同じコンテキストへ反映します。
    /// </summary>
    /// <param name="context">入力シート、計測機能、および各工程の計算結果を保持するレイアウトコンテキストです。</param>
    void Execute(ReportLayoutContext context);
}
