using ExcelRenderer.Markdown;
using ExcelRenderer.Model;

namespace ExcelRenderer.Rendering;

/// <summary>文書全体を Markdown として直接出力する内部シンクの契約です。</summary>
internal interface IMarkdownDocumentOutputSink
{
    /// <summary>解析済み文書を Markdown ファイルへ書き込みます。</summary>
    /// <param name="document">出力する解析済み文書です。</param>
    /// <param name="cancellationToken">書き込みのキャンセルを通知するトークンです。</param>
    /// <returns>Markdown の書き込みが完了したときに完了するタスクを返します。</returns>
    Task WriteMarkdownAsync(ReportDocument document, CancellationToken cancellationToken);

    /// <summary>直前に書き込んだ Markdown のバイト数を取得します。</summary>
    /// <returns>Markdown ファイルのバイト数を返します。</returns>
    long GetMarkdownByteLength();
}

/// <summary>呼び出し元が所有するストリームへ一つの生成物を書き込み、ストリームを閉じません。</summary>
public sealed class SingleStreamOutputSink : IRenderOutputSink
{
    private readonly Stream _stream;
    private bool _opened;

    /// <summary>Initializes a new instance of the <see cref="SingleStreamOutputSink"/> class. 指定したストリームへ書き込むシンクを初期化します。</summary>
    /// <param name="stream">生成物を書き込む、呼び出し元が所有するストリームです。</param>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> が <see langword="null"/> の場合にスローされます。</exception>
    public SingleStreamOutputSink(Stream stream) => _stream = stream ?? throw new ArgumentNullException(nameof(stream));

    /// <summary>唯一の生成物を書き込むためのストリームを開きます。</summary>
    /// <param name="artifact">これから書き込む生成物の識別情報です。</param>
    /// <param name="cancellationToken">オープン処理のキャンセルを通知するトークンです。</param>
    /// <returns>コンストラクターへ渡された書き込み可能なストリームを返します。</returns>
    /// <exception cref="InvalidOperationException">既に生成物を開いている場合にスローされます。</exception>
    /// <exception cref="ArgumentException">指定されたストリームが書き込み可能でない場合にスローされます。</exception>
    public ValueTask<Stream> OpenAsync(ArtifactDescriptor artifact, CancellationToken cancellationToken)
    {
        if (_opened)
        {
            throw new InvalidOperationException("A single-stream sink accepts exactly one artifact.");
        }

        if (!_stream.CanWrite)
        {
            throw new ArgumentException("The output stream must be writable.");
        }

        _opened = true;
        return new(_stream);
    }

    /// <summary>生成物の書き込み完了を受け取ります。ストリームは閉じません。</summary>
    /// <param name="artifact">書き込みが完了した生成物の識別情報です。</param>
    /// <param name="byteLength">生成物へ書き込まれたバイト数です。</param>
    /// <param name="cancellationToken">完了処理のキャンセルを通知するトークンです。</param>
    /// <returns>常に既に完了した値タスクを返します。</returns>
    public ValueTask CompleteAsync(ArtifactDescriptor artifact, long byteLength, CancellationToken cancellationToken) => default;

    /// <summary>生成物の書き込み中断を受け取ります。ストリームは閉じません。</summary>
    /// <param name="artifact">中断された生成物の識別情報です。</param>
    /// <param name="error">生成を中断した例外です。</param>
    /// <param name="cancellationToken">中断処理のキャンセルを通知するトークンです。</param>
    /// <returns>常に既に完了した値タスクを返します。</returns>
    public ValueTask AbortAsync(ArtifactDescriptor artifact, Exception error, CancellationToken cancellationToken) => default;
}

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

/// <summary>従来の Markdown 出力をシンクとして扱う内部アダプターです。</summary>
internal sealed class LegacyMarkdownOutputSink : IRenderOutputSink, IMarkdownDocumentOutputSink
{
    private readonly string _outputPath;
    private readonly MarkdownExportOptions _options;
    private readonly string _documentName;

    /// <summary>Initializes a new instance of the <see cref="LegacyMarkdownOutputSink"/> class. 従来の Markdown 出力 API をシンクとして扱うアダプターを初期化します。</summary>
    /// <param name="outputPath">Markdown ファイルを書き込むパスです。</param>
    /// <param name="options">Markdown 出力の設定です。</param>
    /// <param name="documentName">Markdown 文書名です。</param>
    internal LegacyMarkdownOutputSink(string outputPath, MarkdownExportOptions options, string documentName)
    {
        _outputPath = outputPath;
        _options = options;
        _documentName = documentName;
    }

    /// <summary>文書全体を従来の Markdown エクスポーターで書き込みます。</summary>
    /// <param name="document">出力する解析済み文書です。</param>
    /// <param name="cancellationToken">書き込みのキャンセルを通知するトークンです。</param>
    /// <returns>Markdown の書き込みが完了したときに完了するタスクを返します。</returns>
    public async Task WriteMarkdownAsync(ReportDocument document, CancellationToken cancellationToken) =>
        await new MarkdownExporter().ExportToFileAsync(document, _outputPath, _options, _documentName, cancellationToken)
            .ConfigureAwait(false);

    /// <summary>従来の Markdown ファイルのバイト数を取得します。</summary>
    /// <returns>出力ファイルのバイト数を返します。</returns>
    public long GetMarkdownByteLength() => new FileInfo(_outputPath).Length;

    /// <summary>このアダプターではストリーム出力をサポートしません。</summary>
    /// <param name="artifact">未使用の生成物の識別情報です。</param>
    /// <param name="cancellationToken">未使用のキャンセル通知トークンです。</param>
    /// <returns>常に例外をスローします。</returns>
    /// <exception cref="NotSupportedException">文書全体を直接書き込むアダプターであるため、ストリーム出力を要求した場合にスローされます。</exception>
    public ValueTask<Stream> OpenAsync(ArtifactDescriptor artifact, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The legacy Markdown adapter writes the complete document directly.");

    /// <summary>ストリーム出力の完了通知を無視します。</summary>
    /// <param name="artifact">未使用の生成物の識別情報です。</param>
    /// <param name="byteLength">未使用のバイト数です。</param>
    /// <param name="cancellationToken">未使用のキャンセル通知トークンです。</param>
    /// <returns>常に既に完了した値タスクを返します。</returns>
    public ValueTask CompleteAsync(ArtifactDescriptor artifact, long byteLength, CancellationToken cancellationToken) => default;

    /// <summary>ストリーム出力の中断通知を無視します。</summary>
    /// <param name="artifact">未使用の生成物の識別情報です。</param>
    /// <param name="error">未使用の中断原因です。</param>
    /// <param name="cancellationToken">未使用のキャンセル通知トークンです。</param>
    /// <returns>常に既に完了した値タスクを返します。</returns>
    public ValueTask AbortAsync(ArtifactDescriptor artifact, Exception error, CancellationToken cancellationToken) => default;
}
