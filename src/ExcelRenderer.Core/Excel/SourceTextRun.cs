using ExcelRenderer.Core.Model;

namespace ExcelRenderer.Core.Excel;

/// <summary>A source run independent of any worksheet or cell implementation.</summary>
/// <param name="Text">Original text.</param>
/// <param name="Font">Composed original font appearance.</param>
internal sealed record SourceTextRun(string Text, FontStyle Font);
