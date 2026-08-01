using ReportEngine.Abstractions;
using ReportEngine.Model;

namespace ReportEngine.Layout;

public sealed record RenderDocument(IReadOnlyList<RenderPage> Pages);
