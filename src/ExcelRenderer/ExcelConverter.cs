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
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Fonts;

namespace ExcelRenderer;

/// <summary>Provides convenient, end-to-end Excel conversion operations.</summary>
public static class ExcelConverter
{
    public static async Task ConvertToPdfAsync(string inputPath, string outputPath,
        PdfExportOptions? options = null, CancellationToken cancellationToken = default)
    {
        ValidateInput(inputPath);
        ValidateNewFile(outputPath);
        options ??= new PdfExportOptions();
        cancellationToken.ThrowIfCancellationRequested();
        var sheets = SelectSheets(new ExcelReader().Read(inputPath), options.SheetName);
        EnsureParentDirectory(outputPath);

        await Task.Run(() =>
        {
            using var result = new PdfDocument();
            foreach (var sheet in sheets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var rendered = new MemoryStream();
                new PdfSharpRenderer().Render(CreateCommands(sheet), sheet.PageSettings, rendered);
                rendered.Position = 0;
                using var source = PdfReader.Open(rendered, PdfDocumentOpenMode.Import);
                foreach (var page in source.Pages) result.AddPage(page);
            }
            using var output = File.Create(outputPath);
            result.Save(output, false);
        }, cancellationToken).ConfigureAwait(false);
    }

    public static async Task ConvertToImagesAsync(string inputPath, string outputDirectory,
        ImageExportOptions? options = null, CancellationToken cancellationToken = default)
    {
        ValidateInput(inputPath);
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new ArgumentException("An output directory is required.", nameof(outputDirectory));
        options ??= new ImageExportOptions();
        if (options.Dpi <= 0)
            throw new ArgumentOutOfRangeException(nameof(ImageExportOptions.Dpi), options.Dpi,
                "DPI must be greater than zero.");
        cancellationToken.ThrowIfCancellationRequested();
        var sheets = SelectSheets(new ExcelReader().Read(inputPath), options.SheetName);
        if (Directory.Exists(outputDirectory) && Directory.EnumerateFileSystemEntries(outputDirectory).Any())
            throw new IOException($"Output directory is not empty: {outputDirectory}");
        Directory.CreateDirectory(outputDirectory);

        await Task.Run(() =>
        {
            foreach (var sheet in sheets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sheetName = SanitizeFileName(sheet.Name);
                new PngRenderer().Render(CreateCommands(sheet), sheet.PageSettings, page =>
                    File.Create(Path.Combine(outputDirectory, $"{sheetName}-{page}.png")), options.Dpi);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public static async Task ConvertToMarkdownAsync(string inputPath, string outputPath,
        MarkdownExportOptions? options = null, CancellationToken cancellationToken = default)
    {
        ValidateInput(inputPath);
        ValidateNewFile(outputPath);
        options ??= new MarkdownExportOptions();
        if (string.IsNullOrWhiteSpace(options.ImageDirectoryName) || Path.IsPathRooted(options.ImageDirectoryName) ||
            options.ImageDirectoryName.Split(new[] { '/', '\\' }).Any(part => part == ".."))
            throw new ArgumentException("The image directory must be a relative path below the Markdown output directory.", nameof(options));
        cancellationToken.ThrowIfCancellationRequested();
        var document = new ExcelReader().Read(inputPath);
        var selected = new ReportDocument(SelectSheets(document, options.SheetName));
        EnsureParentDirectory(outputPath);
        await new MarkdownExporter().ExportToFileAsync(selected, outputPath, options,
            Path.GetFileName(inputPath), cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyList<DrawCommand> CreateCommands(ReportSheet sheet)
    {
        GlobalFontSettings.FontResolver ??= new PdfSharpFontResolver(new FontManager());
        var layout = new ReportLayoutEngine(new PdfSharpTextMeasurer()).Layout(sheet);
        return new DrawCommandGeneratorPass().Generate(layout);
    }

    private static ReportSheet[] SelectSheets(ReportDocument document, string? sheetName)
    {
        if (sheetName is null) return document.Sheets.ToArray();
        var sheet = document.Sheets.FirstOrDefault(x => string.Equals(x.Name, sheetName, StringComparison.Ordinal));
        return sheet is null
            ? throw new ArgumentException($"Worksheet was not found: {sheetName}", nameof(sheetName))
            : [sheet];
    }

    private static void ValidateInput(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("An input path is required.", nameof(path));
        if (!File.Exists(path)) throw new FileNotFoundException($"Excel file was not found: {path}", path);
    }

    private static void ValidateNewFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("An output path is required.", nameof(path));
        if (File.Exists(path)) throw new IOException($"Output file already exists: {path}");
    }

    private static void EnsureParentDirectory(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is not null) Directory.CreateDirectory(directory);
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }).ToHashSet();
        var safe = new string(value.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim().Trim('.');
        return safe.Length == 0 ? "sheet" : safe;
    }
}

public sealed record PdfExportOptions
{
    public string? SheetName { get; init; }
}

public sealed record ImageExportOptions
{
    public string? SheetName { get; init; }
    public int Dpi { get; init; } = 144;
}
