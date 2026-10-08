namespace ExcelRenderer.Slim.Model;

/// <summary>
/// 用紙寸法、余白、拡大縮小、ページ数への収め方、および印刷タイトルを表します。
/// </summary>
internal sealed record PageSettings(
    double Width = 595.276,
    double Height = 841.89,
    double MarginLeft = 36,
    double MarginTop = 36,
    double MarginRight = 36,
    double MarginBottom = 36,
    double? Scale = 1,
    int? FitToPagesWide = null,
    int? FitToPagesTall = null,
    IndexRange? TitleRows = null,
    IndexRange? TitleColumns = null)
{
    /// <summary>
    /// Gets or initializes 印刷倍率を明示倍率とページ数への適合のどちらから解決するかを示すモードです。
    /// <see langword="null"/> の場合は、既存の <see cref="Scale"/> 優先の動作を使用します。
    /// </summary>
    public PrintScaleMode? ScaleMode { get; init; }

    /// <summary>Gets the row numbers after which explicit-mode page breaks are inserted.</summary>
    public IReadOnlyList<int> ManualRowBreaks { get; init; } = [];

    /// <summary>Gets the column numbers after which explicit-mode page breaks are inserted.</summary>
    public IReadOnlyList<int> ManualColumnBreaks { get; init; } = [];

    /// <summary>Gets the order in which two-dimensional page bands are emitted.</summary>
    public PrintPageOrder PageOrder { get; init; } = PrintPageOrder.DownThenOver;

    /// <summary>Gets a value indicating whether printed content is horizontally centered.</summary>
    public bool HorizontalCentered { get; init; }

    /// <summary>Gets a value indicating whether printed content is vertically centered.</summary>
    public bool VerticalCentered { get; init; }
}
