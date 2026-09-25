using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ExcelRenderer.Excel;
using ExcelRenderer.Model;

namespace ExcelRenderer.Markdown;

/// <summary>
/// ExcelMarkdownConverter が表すデータと操作を提供します.
/// </summary>
public static class ExcelMarkdownConverter
{
    /// <summary>
    /// ConvertAsync を実行します.
    /// </summary>
        /// <param name="inputPath">inputPath に渡す値です。</param>
        /// <param name="outputDirectory">outputDirectory に渡す値です。</param>
        /// <returns>処理によって得られた結果を返します。</returns>
        /// <param name="options">options に渡す値です。</param>
        /// <param name="cancellationToken">cancellationToken に渡す値です。</param>
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
