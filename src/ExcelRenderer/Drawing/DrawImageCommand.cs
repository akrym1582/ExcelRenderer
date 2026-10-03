using ExcelRenderer.Layout;
using ExcelRenderer.Model;

namespace ExcelRenderer.Drawing;

/// <summary>
/// 指定した矩形内へバイナリ画像を描画するコマンドを表します。
/// </summary>
public sealed record DrawImageCommand(int PageNumber, ReportRect Bounds, byte[] ImageBytes) : DrawCommand(PageNumber)
{
    /// <summary>Gets the source-image crop rectangle.</summary>
    public ImageCrop? Crop { get; init; }

    /// <summary>Gets the clockwise rotation in degrees.</summary>
    public double Rotation { get; init; }

    /// <summary>Gets a value indicating whether the image is flipped horizontally.</summary>
    public bool FlipHorizontal { get; init; }

    /// <summary>Gets a value indicating whether the image is flipped vertically.</summary>
    public bool FlipVertical { get; init; }

    /// <summary>Gets the page-space clipping rectangle.</summary>
    public ReportRect? ClipBounds { get; init; }
}
