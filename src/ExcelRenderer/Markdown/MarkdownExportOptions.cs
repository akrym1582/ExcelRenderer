namespace ExcelRenderer.Markdown;

public sealed record MarkdownExportOptions
{
    public string? SheetName { get; init; }
    public bool DetectLayout { get; init; } = true;
    public bool DetectRegions { get; init; } = true;
    public bool IncludeCellAddresses { get; init; } = true;
    public bool IncludeFormula { get; init; } = true;
    public bool IncludeHiddenLayoutMetadata { get; init; }
    public bool ExportImages { get; init; } = true;
    public bool IncludeImageMetadata { get; init; } = true;
    public bool IncludeNearbyImageText { get; init; } = true;
    public string ImageDirectoryName { get; init; } = "images";
}
