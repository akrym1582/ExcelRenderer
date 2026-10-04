using ExcelRenderer.Layout;
using ExcelRenderer.Model;

namespace ExcelRenderer.Rendering;

/// <summary>描画ページの元文書上および出力上の識別情報を記述します。</summary>
/// <param name="SourceSheetIndex">元ブック内のワークシート番号です。</param>
/// <param name="SourceSheetName">元ワークシートの名前です。</param>
/// <param name="SourcePageNumber">元ワークシート内のページ番号です。</param>
/// <param name="DocumentPageNumber">シートをまたいだ文書全体のページ番号です。</param>
/// <param name="OutputPageNumber">選択後に割り当てられた出力ページ番号です。出力対象外の場合は <see langword="null"/> です。</param>
/// <param name="WidthPoints">ページ幅を PDF ポイントで表した値です。</param>
/// <param name="HeightPoints">ページ高さを PDF ポイントで表した値です。</param>
/// <param name="PixelWidth">PNG 出力時のページ幅をピクセルで表した値です。</param>
/// <param name="PixelHeight">PNG 出力時のページ高さをピクセルで表した値です。</param>
/// <param name="Dpi">ピクセル寸法の算出に使用した解像度です。</param>
public sealed record RenderPageDescriptor(
    int SourceSheetIndex,
    string SourceSheetName,
    int SourcePageNumber,
    int DocumentPageNumber,
    int? OutputPageNumber,
    double WidthPoints,
    double HeightPoints,
    int? PixelWidth = null,
    int? PixelHeight = null,
    double? Dpi = null)
{
    /// <summary>Gets the requested explicit cell range, when supplied.</summary>
    public CellRange? RequestedRange { get; init; }

    /// <summary>Gets the original inclusive body and title cell regions.</summary>
    public IReadOnlyList<CellRange>? SourceCellRanges { get; init; }

    /// <summary>Gets the visible original sheet rectangles for body and repeated titles.</summary>
    public IReadOnlyList<ReportRect>? SourceRegions { get; init; }

    /// <summary>Gets the width before cropping.</summary>
    public double? OriginalWidthPoints { get; init; }

    /// <summary>Gets the height before cropping.</summary>
    public double? OriginalHeightPoints { get; init; }

    /// <summary>Gets the original page-space content crop rectangle.</summary>
    public ReportRect? CropBounds { get; init; }

    /// <summary>Gets the padding added to each side of the content crop.</summary>
    public double? PaddingPoints { get; init; }
}
