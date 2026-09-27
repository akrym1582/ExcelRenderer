namespace ExcelRenderer.Rendering;

/// <summary>既存ファイルを上書きせず、ディレクトリ以下に生成物を保存します。</summary>
public sealed class DirectoryOutputSink : IRenderOutputSink
{
    private readonly string _root;
    private readonly Dictionary<string, Stream> _streams = new(StringComparer.Ordinal);

    /// <summary>Initializes a new instance of the <see cref="DirectoryOutputSink"/> class. 指定したルートディレクトリへ保存するシンクを初期化します。</summary>
    /// <param name="root">生成物を保存するルートディレクトリです。</param>
    /// <exception cref="ArgumentException"><paramref name="root"/> が空、空白、または無効なディレクトリ指定の場合にスローされます。</exception>
    public DirectoryOutputSink(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ArgumentException("An output directory is required.", nameof(root));
        }

        _root = Path.GetFullPath(root);
    }

    /// <summary>生成物の相対名に対応する新規ファイルを開きます。</summary>
    /// <param name="artifact">これから書き込む生成物の識別情報です。</param>
    /// <param name="cancellationToken">オープン処理のキャンセルを通知するトークンです。</param>
    /// <returns>新しく作成した生成物ファイルのストリームを返します。</returns>
    /// <exception cref="ArgumentException">生成物の相対名が空、絶対パス、またはルート外を指す場合にスローされます。</exception>
    /// <exception cref="IOException">ファイルを新規作成できない場合にスローされます。</exception>
    public ValueTask<Stream> OpenAsync(ArtifactDescriptor artifact, CancellationToken cancellationToken)
    {
        var path = GetPath(artifact.RelativeName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        _streams.Add(artifact.ArtifactId, stream);
        return new(stream);
    }

    /// <summary>生成物のストリームを閉じ、ファイルを保持します。</summary>
    /// <param name="artifact">書き込みが完了した生成物の識別情報です。</param>
    /// <param name="byteLength">生成物へ書き込まれたバイト数です。</param>
    /// <param name="cancellationToken">完了処理のキャンセルを通知するトークンです。</param>
    /// <returns>完了処理が終了したときに完了する値タスクを返します。</returns>
    public ValueTask CompleteAsync(ArtifactDescriptor artifact, long byteLength, CancellationToken cancellationToken)
    {
        Close(artifact.ArtifactId);
        return default;
    }

    /// <summary>生成物のストリームを閉じ、途中まで作成したファイルを削除します。</summary>
    /// <param name="artifact">中断された生成物の識別情報です。</param>
    /// <param name="error">生成を中断した例外です。</param>
    /// <param name="cancellationToken">中断処理のキャンセルを通知するトークンです。</param>
    /// <returns>中断処理が終了したときに完了する値タスクを返します。</returns>
    public ValueTask AbortAsync(ArtifactDescriptor artifact, Exception error, CancellationToken cancellationToken)
    {
        var path = GetPath(artifact.RelativeName);
        Close(artifact.ArtifactId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return default;
    }

    private string GetPath(string relativeName)
    {
        if (string.IsNullOrWhiteSpace(relativeName) || Path.IsPathRooted(relativeName))
        {
            throw new ArgumentException("Artifact name must be a relative path.");
        }

        var path = Path.GetFullPath(Path.Combine(_root, relativeName));
        var prefix = _root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ? _root : _root + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Artifact path must remain below the output directory.");
        }

        return path;
    }

    private void Close(string artifactId)
    {
        if (_streams.TryGetValue(artifactId, out var stream))
        {
            _streams.Remove(artifactId);
            stream.Dispose();
        }
    }
}
