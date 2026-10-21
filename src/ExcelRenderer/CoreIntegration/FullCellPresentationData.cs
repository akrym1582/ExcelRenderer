namespace ExcelRenderer.CoreIntegration;

/// <summary>Preserves rich text and visual decorations together through neutral models.</summary>
internal sealed record FullCellPresentationData(FullCellTextData? RichText, ConditionalCellVisual Visual) : Core.Extensibility.ICoreExtensionData
{
    /// <summary>Reads rich text from either the original or combined payload.</summary>
    /// <param name="data">Original or combined cell payload.</param>
    /// <returns>Rich text when available.</returns>
    internal static FullCellTextData? Text(Core.Extensibility.ICoreExtensionData? data) => data is FullCellPresentationData presentation ? presentation.RichText : data as FullCellTextData;

    /// <summary>Reads the immutable visual result without changing rich text.</summary>
    /// <param name="data">Cell extension payload.</param>
    /// <returns>The decoration metadata.</returns>
    internal static ConditionalCellVisual? Decoration(Core.Extensibility.ICoreExtensionData? data) => (data as FullCellPresentationData)?.Visual;

    /// <summary>Snapshots a public cell at the neutral model boundary.</summary>
    /// <param name="cell">Public cell with optional rich text and visual decorations.</param>
    /// <returns>The immutable combined payload.</returns>
    internal static Core.Extensibility.ICoreExtensionData? Create(Model.ReportCell cell)
    {
        var text = FullCellTextData.Create(cell);
        return cell.ConditionalVisual is { } visual ? new FullCellPresentationData(text, visual) : text;
    }
}
