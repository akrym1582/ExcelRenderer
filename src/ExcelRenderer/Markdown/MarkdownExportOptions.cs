namespace ExcelRenderer.Markdown;

/// <summary>
/// MarkdownExportOptions が表すデータと操作を提供します.
/// </summary>
public sealed record MarkdownExportOptions
{
    /// <summary>
    /// Gets the value. 対応する値を取得または設定します.
    /// </summary>
    public string? SheetName { get; init; }

    /// <summary>
    /// Gets a value indicating whether the value. 対応する値を取得または設定します.
    /// </summary>
    public bool DetectLayout { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether the value. 対応する値を取得または設定します.
    /// </summary>
    public bool DetectRegions { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether the value. 対応する値を取得または設定します.
    /// </summary>
    public bool IncludeCellAddresses { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether the value. 対応する値を取得または設定します.
    /// </summary>
    public bool IncludeFormula { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether the value. 対応する値を取得または設定します.
    /// </summary>
    public bool IncludeHiddenLayoutMetadata { get; init; }

    /// <summary>
    /// Gets a value indicating whether the value. 対応する値を取得または設定します.
    /// </summary>
    public bool ExportImages { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether the value. 対応する値を取得または設定します.
    /// </summary>
    public bool IncludeImageMetadata { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether the value. 対応する値を取得または設定します.
    /// </summary>
    public bool IncludeNearbyImageText { get; init; } = true;

    /// <summary>
    /// Gets the value. 対応する値を取得または設定します.
    /// </summary>
    public string ImageDirectoryName { get; init; } = "images";
}
