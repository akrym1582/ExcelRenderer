using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>
/// ページ上に配置された画像の描画矩形、バイナリデータ、および重なり順を表します。
/// </summary>
public sealed record RenderImage(ReportRect Bounds, byte[] ImageBytes, int ZIndex = 0)
{
    /// <summary>Gets the source-image crop rectangle.</summary>
    public ImageCrop? Crop { get; init; }

    /// <summary>Gets the clockwise rotation in degrees.</summary>
    public double Rotation { get; init; }

    /// <summary>Gets a value indicating whether the image is flipped horizontally.</summary>
    public bool FlipHorizontal { get; init; }

    /// <summary>Gets a value indicating whether the image is flipped vertically.</summary>
    public bool FlipVertical { get; init; }

    /// <summary>Gets the page-space body viewport that clips this object.</summary>
    public ReportRect? ClipBounds { get; init; }
}
