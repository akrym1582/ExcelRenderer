using ExcelRenderer.Core.Model;

namespace ExcelRenderer.CoreIntegration;

/// <summary>Immutable numeric decoration results; saved values and formulas remain unchanged.</summary>
internal sealed record ConditionalCellVisual(ConditionalDataBar? DataBar, ConditionalIcon? Icon)
{
    /// <summary>Gets a value indicating whether either winning decoration suppresses text.</summary>
    internal bool HideValue => DataBar?.ShowValue == false || Icon?.ShowValue == false;
}
