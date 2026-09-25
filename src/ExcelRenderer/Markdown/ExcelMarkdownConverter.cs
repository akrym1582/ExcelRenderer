using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ExcelRenderer.Excel;
using ExcelRenderer.Model;

namespace ExcelRenderer.Markdown;

/// <summary>
/// Excel ブックを読み込み、ワークシートの内容を Markdown ファイルへ変換します。
/// </summary>
public static class ExcelMarkdownConverter
{
    /// <summary>
    /// 指定した Excel ファイルを解析し、文書名に基づく Markdown ファイルと埋め込み画像を出力します。
    /// </summary>
    /// <param name="inputPath">読み込む Excel ファイルのパス。</param>
    /// <param name="outputDirectory">Markdown ファイルと画像を配置するディレクトリのパス。</param>
    /// <param name="options">対象シートや出力内容を指定するオプション。<see langword="null"/> の場合は既定値を使用します。</param>
    /// <param name="cancellationToken">出力処理のキャンセルを通知するトークン。</param>
    /// <returns>Markdown ファイルと画像の書き出しが完了したときに完了するタスク。</returns>
    public static Task ConvertAsync(
        string inputPath,
        string outputDirectory,
        MarkdownExportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var document = new ExcelReader().Read(inputPath);
        return new MarkdownExporter().ExportAsync(
            document,
            outputDirectory,
            options,
            Path.GetFileName(inputPath),
            cancellationToken);
    }
}
