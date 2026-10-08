namespace ExcelRenderer.Slim;

/// <summary>変換元ブックを準備するときに使用する上限と方針です。</summary>
public sealed record WorkbookInputOptions
{
    /// <summary>Gets the maximum number of bytes retained in memory. 入力をメモリ上に保持できる最大バイト数です。</summary>
    public long MemoryThresholdBytes { get; init; } = 16 * 1024 * 1024;

    /// <summary>Gets the maximum number of accepted input bytes. 受け入れる入力ストリームの最大バイト数です。</summary>
    public long MaxInputBytes { get; init; } = 128 * 1024 * 1024;

    /// <summary>Gets a value indicating whether temporary files may be used after the memory limit is exceeded. メモリ上限を超えた入力について、一時ファイルの利用を許可するかどうかを示します。</summary>
    public bool AllowTemporaryFiles { get; init; }

    /// <summary>Gets the directory for temporary files. 一時ファイルを作成するディレクトリです。<see langword="null"/> の場合は既定の場所を使用します。</summary>
    public string? TemporaryDirectory { get; init; }

    /// <summary>Gets the maximum number of entries allowed in the input ZIP. 入力 ZIP に含められるエントリ数の最大値です。</summary>
    public int MaxZipEntryCount { get; init; } = 10_000;

    /// <summary>Gets the maximum total size of expanded ZIP entries. 展開後の ZIP エントリ合計サイズの最大バイト数です。</summary>
    public long MaxUncompressedZipBytes { get; init; } = 512 * 1024 * 1024;
}
