using ExcelRenderer.Layout;
using ExcelRenderer.Model;

namespace ExcelRenderer.Rendering;

/// <summary>内容を開く前の生成物を識別します。</summary>
/// <param name="ArtifactId">変換結果内で生成物を一意に識別する ID です。</param>
/// <param name="Kind">生成物の論理的な種類です。</param>
/// <param name="MediaType">生成物の MIME タイプです。</param>
/// <param name="RelativeName">出力先のルートからの相対パスです。</param>
/// <param name="SourcePageNumber">対応する元文書ページ番号です。ページ単位でない生成物では <see langword="null"/> です。</param>
/// <param name="OutputPageNumber">選択後の出力ページ番号です。ページ単位でない生成物では <see langword="null"/> です。</param>
/// <param name="IsContinuous">生成物が連続レイアウトの場合は <see langword="true"/> です。</param>
/// <param name="SourceSheetName">生成物の元になったシート名です。</param>
/// <param name="WidthPoints">生成物の幅をポイント単位で表します。</param>
/// <param name="HeightPoints">生成物の高さをポイント単位で表します。</param>
/// <param name="PixelWidth">ラスター生成物の幅をピクセル単位で表します。</param>
/// <param name="PixelHeight">ラスター生成物の高さをピクセル単位で表します。</param>
public sealed record ArtifactDescriptor(
    string ArtifactId,
    string Kind,
    string MediaType,
    string RelativeName,
    int? SourcePageNumber = null,
    int? OutputPageNumber = null,
    bool IsContinuous = false,
    string? SourceSheetName = null,
    double? WidthPoints = null,
    double? HeightPoints = null,
    int? PixelWidth = null,
    int? PixelHeight = null)
{
    /// <summary>Gets the source request range when explicitly selected.</summary>
    public CellRange? RequestedRange { get; init; }

    /// <summary>Gets the original width before trimming.</summary>
    public double? OriginalWidthPoints { get; init; }

    /// <summary>Gets the original height before trimming.</summary>
    public double? OriginalHeightPoints { get; init; }

    /// <summary>Gets the original page-space crop rectangle.</summary>
    public ReportRect? CropBounds { get; init; }

    /// <summary>Gets the padding on each crop side.</summary>
    public double? PaddingPoints { get; init; }
}
