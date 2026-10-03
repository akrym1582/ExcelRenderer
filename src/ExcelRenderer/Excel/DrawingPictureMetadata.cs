using ExcelRenderer.Model;

namespace ExcelRenderer.Excel;

/// <summary>Preserves DrawingML metadata associated with a worksheet picture.</summary>
internal sealed record DrawingPictureMetadata(
    DrawingAnchor Anchor,
    ImageCrop? Crop,
    double Rotation,
    bool FlipHorizontal,
    bool FlipVertical,
    int ZIndex);
