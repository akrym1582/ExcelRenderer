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
