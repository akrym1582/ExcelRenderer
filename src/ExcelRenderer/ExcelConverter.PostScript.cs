using ExcelRenderer.Rendering;

namespace ExcelRenderer;

/// <summary>Converts Excel workbooks to vector documents.</summary>
public static partial class ExcelConverter
{
    /// <summary>Converts selected worksheets into one new multipage PostScript file.</summary>
    /// <param name="inputPath">The source XLSX path.</param>
    /// <param name="outputPath">A new PS file; existing files are rejected.</param>
    /// <param name="options">Font, sheet, formula and buffering options.</param>
    /// <param name="cancellationToken">The conversion token.</param>
    /// <returns>A task completed after the document has been committed.</returns>
    public static async Task ConvertToPostScriptAsync(string inputPath, string outputPath, PostScriptExportOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new PostScriptExportOptions();
        (options.ReadOptions ?? throw new ArgumentException("ReadOptions are required.", nameof(options))).Validate();
        ValidateInput(inputPath);
        ValidateNewFile(outputPath);
        await using var input = File.OpenRead(inputPath);
        await RenderAsync(
            input,
            CreateLegacyRequest(OutputFormat.PostScript, ResolveSheetNames(options.SheetName, options.SheetNames), fontOptions: options.FontOptions) with
            {
                Buffering = options.Buffering,
                ReadOptions = options.ReadOptions,
            },
            new NewFileOutputSink(outputPath),
            cancellationToken).ConfigureAwait(false);
    }
}
