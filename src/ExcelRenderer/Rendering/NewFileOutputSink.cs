namespace ExcelRenderer.Rendering;

/// <summary>レンダラーが一つの生成物を開いた時点で新しいファイルを作成します。</summary>
internal sealed class NewFileOutputSink : IRenderOutputSink
{
    private readonly string _path;
    private Stream? _stream;

    /// <summary>Initializes a new instance of the <see cref="NewFileOutputSink"/> class. 指定したファイルパスへ書き込むシンクを初期化します。</summary>
    /// <param name="path">新規作成するファイルのパスです。</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> が <see langword="null"/> の場合にスローされます。</exception>
    internal NewFileOutputSink(string path) => _path = path ?? throw new ArgumentNullException(nameof(path));

    /// <summary>新規ファイルへ生成物を書き込むストリームを開きます。</summary>
    /// <param name="artifact">これから書き込む生成物の識別情報です。</param>
    /// <param name="cancellationToken">オープン処理のキャンセルを通知するトークンです。</param>
    /// <returns>新しく作成したファイルのストリームを返します。</returns>
    /// <exception cref="IOException">ファイルを新規作成できない場合にスローされます。</exception>
    public ValueTask<Stream> OpenAsync(ArtifactDescriptor artifact, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(_path));
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        _stream = new FileStream(_path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        return new(_stream);
    }

    /// <summary>ファイルを閉じ、完了した生成物を保持します。</summary>
    /// <param name="artifact">書き込みが完了した生成物の識別情報です。</param>
    /// <param name="byteLength">生成物へ書き込まれたバイト数です。</param>
    /// <param name="cancellationToken">完了処理のキャンセルを通知するトークンです。</param>
    /// <returns>完了処理が終了したときに完了する値タスクを返します。</returns>
    public ValueTask CompleteAsync(ArtifactDescriptor artifact, long byteLength, CancellationToken cancellationToken)
    {
        _stream?.Dispose();
        _stream = null;
        return default;
    }

    /// <summary>ファイルを閉じ、失敗した生成物を削除します。</summary>
    /// <param name="artifact">中断された生成物の識別情報です。</param>
    /// <param name="error">生成を中断した例外です。</param>
    /// <param name="cancellationToken">中断処理のキャンセルを通知するトークンです。</param>
    /// <returns>中断処理が終了したときに完了する値タスクを返します。</returns>
    public ValueTask AbortAsync(ArtifactDescriptor artifact, Exception error, CancellationToken cancellationToken)
    {
        _stream?.Dispose();
        _stream = null;
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }

        return default;
    }
}
