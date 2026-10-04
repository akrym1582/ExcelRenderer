namespace ExcelRenderer.Rendering;

/// <summary>変換対象とするワークシートとページを選択します。</summary>
public sealed record SelectionOptions
{
    /// <summary>Gets one explicit rectangle per selected worksheet.</summary>
    public IReadOnlyList<SheetRangeSelection>? Ranges { get; init; }

    /// <summary>Gets the maximum total number of cells in explicit ranges.</summary>
    public long MaxRangeCells { get; init; } = 1_000_000;

    /// <summary>Gets the worksheet names to select. 名前で選択するワークシートの一覧です。<see langword="null"/> の場合はすべてを選択します。</summary>
    public IReadOnlyList<string>? SheetNames { get; init; }

    /// <summary>Gets the document page numbers to select. 文書ページ番号で選択するページの一覧です。<see langword="null"/> の場合はすべてを選択します。</summary>
    public IReadOnlyList<int>? Pages { get; init; }
}
