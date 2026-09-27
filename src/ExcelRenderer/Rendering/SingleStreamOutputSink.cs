namespace ExcelRenderer.Rendering;

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
