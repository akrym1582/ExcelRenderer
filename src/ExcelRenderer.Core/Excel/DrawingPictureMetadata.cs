using ExcelRenderer.Core.Model;

namespace ExcelRenderer.Core.Excel;

/// <summary>Preserves DrawingML metadata associated with a worksheet picture.</summary>
internal sealed record DrawingPictureMetadata(
    DrawingAnchor Anchor,
    int ZIndex);
