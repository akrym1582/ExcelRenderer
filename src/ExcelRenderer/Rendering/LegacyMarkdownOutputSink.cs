using ExcelRenderer.Markdown;
using ExcelRenderer.Model;

namespace ExcelRenderer.Rendering;

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
