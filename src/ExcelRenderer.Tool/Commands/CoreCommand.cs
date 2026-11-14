using System.CommandLine;
using ExcelRenderer.Fonts;

namespace ExcelRenderer.Tool.Commands;

/// <summary>Builds the PDF-only Core conversion command.</summary>
public static class CoreCommand
{
    /// <summary>Creates the core command using an explicit or bundled drawing font.</summary>
    /// <returns>The Core PDF conversion command.</returns>
    public static Command Create()
    {
        var input = CommandSupport.InputArgument();
        var output = CommandSupport.OutputOption("Path to a new output PDF file.");
        var font = new Option<string>("--font-file") { Description = "Static TrueType font file or collection; omitted uses bundled Noto Sans JP." };
        var fontIndex = new Option<int>("--font-collection-index") { Description = "Zero-based font collection face index." };
        var sheet = new Option<string[]>("--sheet") { Description = "Worksheet names in output order (repeatable).", AllowMultipleArgumentsPerToken = true };
        var metrics = new Option<string>("--column-width-metrics") { Description = "Column metrics: source (default), fallback (skip original-font lookup) or libreoffice.", DefaultValueFactory = _ => "source" };
        var mdw = new Option<double?>("--column-width-mdw") { Description = "Explicit Normal digit width in pixels at 96 DPI." };
        var fallbackMdw = new Option<double?>("--column-width-fallback-mdw") { Description = "Normal digit width at 96 DPI used only if the original font is unavailable; overrides the fallback policy." };
        var fallbackMetrics = new Option<string>("--column-width-fallback-metrics") { Description = "Missing-source metrics: fixed (default, 8px) or font (measure the drawing substitute).", DefaultValueFactory = _ => "fixed" };
        var layoutFont = new Option<string?>("--layout-font") { Description = "Original font file used only for column measurements." };
        var layoutFontIndex = new Option<int?>("--layout-font-index") { Description = "Zero-based measurement face index; requires --layout-font." };
        var noSystemFonts = new Option<bool>("--no-system-fonts") { Description = "Disable system lookup of the original Normal font for column measurements." };
        var data = MappingCommandSupport.DataOption();
        var command = new Command("core", "Convert Excel to PDF using ExcelRenderer.Core and an explicit or bundled font (saved values only).")
        {
            input, output, font, fontIndex, sheet, data, metrics, mdw, fallbackMdw, fallbackMetrics, layoutFont, layoutFontIndex, noSystemFonts,
        };
        command.SetAction((result, cancellationToken) => CommandSupport.RunAsync(result, () => MappingCommandSupport.WithInputAsync(
            result.GetValue(input)!,
            result.GetValue(data),
            result.GetValue(output)!,
            mappedInput => ConvertAsync(
                mappedInput,
                result.GetValue(output)!,
                new Core.PdfExportOptions
                {
                    ReadOptions = new()
                    {
                        UseLibreOfficeColumnWidths = result.GetValue(metrics) switch
                        {
                            "source" or "fallback" => false,
                            "libreoffice" => true,
                            _ => throw new ArgumentException("--column-width-metrics must be source, fallback or libreoffice."),
                        },
                        UseSourceFontForColumnWidths = result.GetValue(metrics) != "fallback",
                        MaximumDigitWidthPixels96Dpi = result.GetValue(mdw),
                        FallbackMaximumDigitWidthPixels96Dpi = CommandSupport.GetFallbackColumnWidth(result, fallbackMdw, fallbackMetrics),
                        SourceFontFile = result.GetValue(layoutFont),
                        SourceFontCollectionIndex = result.GetValue(layoutFontIndex),
                        UseSystemFontsForColumnWidths = !result.GetValue(noSystemFonts),
                    },
                    FontFilePath = result.GetValue(font) ?? string.Empty,
                    FontCollectionIndex = result.GetValue(fontIndex),
                    SheetNames = CommandSupport.GetSheetNames(result.GetValue(sheet)),
                },
                cancellationToken),
            cancellationToken)));
        return command;
    }

    private static async Task ConvertAsync(string inputPath, string outputPath, Core.PdfExportOptions options, CancellationToken cancellationToken)
    {
        outputPath = Path.GetFullPath(outputPath);
        if (File.Exists(outputPath))
        {
            throw new IOException($"Output file already exists: {outputPath}");
        }

        await using var input = File.OpenRead(inputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var temporary = outputPath + $".{Guid.NewGuid():N}.tmp";
        string? bundledFont = null;
        try
        {
            if (string.IsNullOrWhiteSpace(options.FontFilePath))
            {
                if (options.FontCollectionIndex != 0)
                {
                    throw new ArgumentException("--font-collection-index requires --font-file.");
                }

                var resolved = new FontManager(new FontOptions
                {
                    AllowSystemFonts = false,
                    FallbackFamilies = [],
                }).Resolve(new FontRequest("Noto Sans JP"));
                bundledFont = temporary + ".ttf";
                if (resolved.FontData is { } bytes)
                {
                    await File.WriteAllBytesAsync(bundledFont, bytes, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    File.Copy(resolved.FilePath, bundledFont);
                }

                options = options with { FontFilePath = bundledFont, FontCollectionIndex = resolved.FontCollectionIndex };
            }

            Core.ConversionResult conversion;
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                conversion = await Core.ExcelConverter.ConvertAsync(input, output, options, cancellationToken).ConfigureAwait(false);
            }

            foreach (var diagnostic in conversion.Diagnostics)
            {
                var resolved = diagnostic.Code == "MaximumDigitWidthResolved";
                Console.WriteLine($"{(resolved ? "Info" : "Warning")}: {diagnostic.Code} [{diagnostic.SheetName ?? "workbook"}, page {diagnostic.PageNumber}]: {diagnostic.Message}");
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, outputPath);
            Console.WriteLine($"Core PDF: {conversion.PageCount} pages; {conversion.Diagnostics.Count} diagnostics; output: {outputPath}");
        }
        finally
        {
            File.Delete(temporary);
            if (bundledFont is not null)
            {
                File.Delete(bundledFont);
            }
        }
    }
}
