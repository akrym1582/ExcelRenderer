using System.Threading;
using System.Threading.Tasks;
using ExcelRenderer.Drawing;
using ExcelRenderer.Excel;
using ExcelRenderer.Fonts;
using ExcelRenderer.Layout;
using ExcelRenderer.Markdown;
using ExcelRenderer.Model;
using ExcelRenderer.PdfSharp;
using ExcelRenderer.SkiaSharp;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace ExcelRenderer;

/// <summary>Excel ファイルを PDF、PNG 画像、または Markdown 文書へ変換する一連の操作を提供します。</summary>
public static class ExcelConverter
{
    /// <summary>Excel ブックのワークシートをレイアウトし、単一の PDF 文書へ非同期に変換します。</summary>
    /// <param name="inputPath">読み取る Excel ファイルのパスです。</param>
    /// <param name="outputPath">変換した PDF 文書を新規作成するファイルパスです。</param>
    /// <param name="options">出力対象のワークシートを指定する設定です。省略時はすべてのワークシートを出力します。</param>
    /// <param name="cancellationToken">変換処理のキャンセルを通知するトークンです。</param>
    /// <returns>PDF ファイルの書き込みが完了したときに完了するタスクを返します。</returns>
    public static async Task ConvertToPdfAsync(
        string inputPath,
        string outputPath,
        PdfExportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ValidateInput(inputPath);
        ValidateNewFile(outputPath);
        options ??= new PdfExportOptions();
        cancellationToken.ThrowIfCancellationRequested();
        var sheets = SelectSheets(new ExcelReader().Read(inputPath), options.SheetName);
        EnsureParentDirectory(outputPath);

        await Task.Run(
            () =>
            {
                using var result = new PdfDocument();
                foreach (var sheet in sheets)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var rendered = new MemoryStream();
                    new PdfSharpRenderer().Render(CreateCommands(sheet), sheet.PageSettings, rendered);
                    rendered.Position = 0;
                    using var source = PdfReader.Open(rendered, PdfDocumentOpenMode.Import);
                    foreach (var page in source.Pages)
                    {
                        result.AddPage(page);
                    }
                }

                using var output = File.Create(outputPath);
                result.Save(output, false);
            },
            cancellationToken)
        .ConfigureAwait(false);
    }

    /// <summary>Excel ブックのワークシートをレイアウトし、各ページを PNG 画像へ非同期に変換します。</summary>
    /// <param name="inputPath">読み取る Excel ファイルのパスです。</param>
    /// <param name="outputDirectory">ワークシート名とページ番号を含む PNG ファイルを新規作成する空のディレクトリです。</param>
    /// <param name="options">出力対象のワークシートと画像解像度を指定する設定です。省略時は既定値を使用します。</param>
    /// <param name="cancellationToken">変換処理のキャンセルを通知するトークンです。</param>
    /// <returns>対象ページすべての PNG ファイルの書き込みが完了したときに完了するタスクを返します。</returns>
    public static async Task ConvertToImagesAsync(
        string inputPath,
        string outputDirectory,
        ImageExportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ValidateInput(inputPath);
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("An output directory is required.", nameof(outputDirectory));
        }

        options ??= new ImageExportOptions();
        if (options.Dpi <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ImageExportOptions.Dpi),
                options.Dpi,
                "DPI must be greater than zero.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var sheets = SelectSheets(new ExcelReader().Read(inputPath), options.SheetName);
        if (Directory.Exists(outputDirectory) && Directory.EnumerateFileSystemEntries(outputDirectory).Any())
        {
            throw new IOException($"Output directory is not empty: {outputDirectory}");
        }

        Directory.CreateDirectory(outputDirectory);

        await Task.Run(
            () =>
            {
                foreach (var sheet in sheets)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var sheetName = SanitizeFileName(sheet.Name);
                    new PngRenderer().Render(
                        CreateCommands(sheet),
                        sheet.PageSettings,
                        page =>
                        File.Create(Path.Combine(
                            outputDirectory,
                            $"{sheetName}-{page}.png")),
                        options.Dpi);
                }
            },
            cancellationToken)
        .ConfigureAwait(false);
    }

    /// <summary>Excel ブックの表領域を Markdown の表または HTML として表現し、埋め込み画像とともに非同期に出力します。</summary>
    /// <param name="inputPath">読み取る Excel ファイルのパスです。</param>
    /// <param name="outputPath">変換した Markdown 文書を新規作成するファイルパスです。</param>
    /// <param name="options">対象ワークシート、画像格納先、およびレイアウト解析方法を指定する設定です。省略時は既定値を使用します。</param>
    /// <param name="cancellationToken">変換処理のキャンセルを通知するトークンです。</param>
    /// <returns>Markdown 文書と付随する画像ファイルの書き込みが完了したときに完了するタスクを返します。</returns>
    public static async Task ConvertToMarkdownAsync(
        string inputPath,
        string outputPath,
        MarkdownExportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ValidateInput(inputPath);
        ValidateNewFile(outputPath);
        options ??= new MarkdownExportOptions();
        if (string.IsNullOrWhiteSpace(options.ImageDirectoryName) || Path.IsPathRooted(options.ImageDirectoryName) ||
            options.ImageDirectoryName.Split(new[] { '/', '\\' }).Any(part => part == ".."))
        {
            throw new ArgumentException("The image directory must be a relative path below the Markdown output directory.", nameof(options));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var document = new ExcelReader().Read(inputPath);
        var selected = new ReportDocument(SelectSheets(document, options.SheetName));
        EnsureParentDirectory(outputPath);
        await new MarkdownExporter().ExportToFileAsync(
            selected,
            outputPath,
            options,
            Path.GetFileName(inputPath),
            cancellationToken)
        .ConfigureAwait(false);
    }

    private static IReadOnlyList<DrawCommand> CreateCommands(ReportSheet sheet)
    {
        GlobalFontSettings.FontResolver ??= new PdfSharpFontResolver(new FontManager());
        var layout = new ReportLayoutEngine(new PdfSharpTextMeasurer()).Layout(sheet);
        return new DrawCommandGeneratorPass().Generate(layout);
    }

    private static ReportSheet[] SelectSheets(ReportDocument document, string? sheetName)
    {
        if (sheetName is null)
        {
            return document.Sheets.ToArray();
        }

        var sheet = document.Sheets.FirstOrDefault(x => string.Equals(x.Name, sheetName, StringComparison.Ordinal));
        return sheet is null
            ? throw new ArgumentException($"Worksheet was not found: {sheetName}", nameof(sheetName))
            : [sheet];
    }

    private static void ValidateInput(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("An input path is required.", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Excel file was not found: {path}", path);
        }
    }

    private static void ValidateNewFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("An output path is required.", nameof(path));
        }

        if (File.Exists(path))
        {
            throw new IOException($"Output file already exists: {path}");
        }
    }

    private static void EnsureParentDirectory(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }).ToHashSet();
        var safe = new string(value.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim().Trim('.');
        return safe.Length == 0 ? "sheet" : safe;
    }
}
