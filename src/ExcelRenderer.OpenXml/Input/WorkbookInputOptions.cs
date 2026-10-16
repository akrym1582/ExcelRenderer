namespace ExcelRenderer.OpenXml.Input;

/// <summary>変換元ブックを準備するときに使用する上限と方針です。</summary>
public sealed class WorkbookInputOptions
{
    /// <summary>Gets or sets the maximum number of bytes retained in memory. 入力をメモリ上に保持できる最大バイト数です。</summary>
    public long MemoryThresholdBytes { get; set; } = 16 * 1024 * 1024;

    /// <summary>Gets or sets the maximum number of accepted input bytes. 受け入れる入力ストリームの最大バイト数です。</summary>
    public long MaxInputBytes { get; set; } = 128 * 1024 * 1024;

    /// <summary>Gets or sets a value indicating whether temporary files may be used after the memory limit is exceeded. メモリ上限を超えた入力について、一時ファイルの利用を許可するかどうかを示します。</summary>
    public bool AllowTemporaryFiles { get; set; }

    /// <summary>Gets or sets the directory for temporary files. 一時ファイルを作成するディレクトリです。<see langword="null"/> の場合は既定の場所を使用します。</summary>
    public string? TemporaryDirectory { get; set; }

    /// <summary>Gets or sets the maximum number of entries allowed in the input ZIP. 入力 ZIP に含められるエントリ数の最大値です。</summary>
    public int MaxZipEntryCount { get; set; } = 10_000;

    /// <summary>Gets or sets the maximum total size of expanded ZIP entries. 展開後の ZIP エントリ合計サイズの最大バイト数です。</summary>
    public long MaxUncompressedZipBytes { get; set; } = 512 * 1024 * 1024;
}
