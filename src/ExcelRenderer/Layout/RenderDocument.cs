using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>
/// レイアウト計算によって生成された描画対象ページの集合を表します。
/// </summary>
public sealed record RenderDocument(IReadOnlyList<RenderPage> Pages);
