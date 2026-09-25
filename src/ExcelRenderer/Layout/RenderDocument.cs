using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>
/// RenderDocument が表すデータと操作を提供します.
/// </summary>
public sealed record RenderDocument(IReadOnlyList<RenderPage> Pages);
