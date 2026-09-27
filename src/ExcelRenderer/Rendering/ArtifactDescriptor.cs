namespace ExcelRenderer.Rendering;

/// <summary>内容を開く前の生成物を識別します。</summary>
/// <param name="ArtifactId">変換結果内で生成物を一意に識別する ID です。</param>
/// <param name="Kind">生成物の論理的な種類です。</param>
/// <param name="MediaType">生成物の MIME タイプです。</param>
/// <param name="RelativeName">出力先のルートからの相対パスです。</param>
/// <param name="SourcePageNumber">対応する元文書ページ番号です。ページ単位でない生成物では <see langword="null"/> です。</param>
/// <param name="OutputPageNumber">選択後の出力ページ番号です。ページ単位でない生成物では <see langword="null"/> です。</param>
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
    int? PixelHeight = null);
