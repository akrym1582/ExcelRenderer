namespace ExcelRenderer.Model;

/// <summary>Represents DrawingML source-image crop percentages.</summary>
/// <param name="Left">Left crop as a fraction of source width.</param>
/// <param name="Top">Top crop as a fraction of source height.</param>
/// <param name="Right">Right crop as a fraction of source width.</param>
/// <param name="Bottom">Bottom crop as a fraction of source height.</param>
public sealed record ImageCrop(double Left, double Top, double Right, double Bottom);
