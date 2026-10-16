namespace ExcelRenderer.Core.Excel;

/// <summary>Neutral original run data captured only when the product requests it.</summary>
/// <param name="Runs">Original text and composed source fonts.</param>
/// <param name="UnsupportedFormatting">Whether unsupported source formatting is present.</param>
internal sealed record SourceCellText(IReadOnlyList<SourceTextRun> Runs, bool UnsupportedFormatting);
