using ExcelRenderer.Abstractions;
using ExcelRenderer.Core.Extensibility;

namespace ExcelRenderer.CoreIntegration;

/// <summary>Borrows finalized font runs; their conversion session retains native ownership.</summary>
/// <param name="Line">The original immutable finalized line.</param>
internal sealed record FullLineData(TextLayoutLine Line) : ICoreExtensionData
{
    /// <summary>Gets borrowed finalized runs.</summary>
    internal IReadOnlyList<TextLayoutRun> Runs => Line.Runs;
}
