namespace ExcelRenderer.Model;

/// <summary>
/// セルを基準とする位置、寸法、画像データ、重なり順、および画像メタデータを表します。
/// </summary>
public sealed record ReportImage(
    CellAddress Anchor,
    double OffsetX,
    double OffsetY,
    double Width,
    double Height,
    byte[] ImageBytes,
    int ZIndex = 0,
    string? Name = null,
    string? ContentType = null,
    string? Extension = null)
{
    /// <summary>Gets the original DrawingML anchor metadata when available.</summary>
    public DrawingAnchor? DrawingAnchor { get; init; }

    /// <summary>Gets the source-image crop rectangle.</summary>
    public ImageCrop? Crop { get; init; }

    /// <summary>Gets the clockwise image rotation in degrees.</summary>
    public double Rotation { get; init; }

    /// <summary>Gets a value indicating whether the image is flipped horizontally.</summary>
    public bool FlipHorizontal { get; init; }

    /// <summary>Gets a value indicating whether the image is flipped vertically.</summary>
    public bool FlipVertical { get; init; }
}
