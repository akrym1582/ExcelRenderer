namespace ExcelRenderer.Markdown;

/// <summary>
/// Excel 文書を Markdown へ出力する際の動作を指定します。
/// </summary>
public sealed record MarkdownExportOptions
{
    /// <summary>
    /// Gets the worksheet name. 出力対象とするワークシート名を取得します。<see langword="null"/> の場合はすべてのシートを出力します。
    /// </summary>
    public string? SheetName { get; init; }

    /// <summary>
    /// Gets a value indicating whether layout detection is enabled. セル間の空白を使用してレイアウトを分割するかどうかを示す値を取得します。
    /// </summary>
    public bool DetectLayout { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether region detection is enabled. 分割したレイアウトから内容領域を検出して分類するかどうかを示す値を取得します。
    /// </summary>
    public bool DetectRegions { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether region headings include cell addresses. 各領域の見出しに Excel のセル範囲を含めるかどうかを示す値を取得します。
    /// </summary>
    public bool IncludeCellAddresses { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether formulas are included. 数式を持つセルの出力に数式を含めるかどうかを示す値を取得します。
    /// </summary>
    public bool IncludeFormula { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether hidden layout metadata is included. レイアウト解析結果を非表示の HTML コメントとして含めるかどうかを示す値を取得します。
    /// </summary>
    public bool IncludeHiddenLayoutMetadata { get; init; }

    /// <summary>
    /// Gets a value indicating whether embedded images are exported. ワークシート内の埋め込み画像をファイルへ書き出すかどうかを示す値を取得します。
    /// </summary>
    public bool ExportImages { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether image metadata is included. 出力した画像のアンカー位置、寸法、および代替テキストを記載するかどうかを示す値を取得します。
    /// </summary>
    public bool IncludeImageMetadata { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether nearby cell text describes images. 画像の近くにあるセルの文字列を画像の説明として含めるかどうかを示す値を取得します。
    /// </summary>
    public bool IncludeNearbyImageText { get; init; } = true;

    /// <summary>
    /// Gets the image output directory name. Markdown ファイルを基準とした画像出力先ディレクトリ名を取得します。
    /// </summary>
    public string ImageDirectoryName { get; init; } = "images";
}
