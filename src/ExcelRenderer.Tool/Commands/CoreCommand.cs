using System.CommandLine;

namespace ExcelRenderer.Tool.Commands;

/// <summary>Builds the PDF-only Core conversion command.</summary>
public static class CoreCommand
{
    /// <summary>Creates the core command using one caller-supplied font.</summary>
    /// <returns>The Core PDF conversion command.</returns>
    public static Command Create()
    {
        var input = CommandSupport.InputArgument();
        var output = CommandSupport.OutputOption("Path to a new output PDF file.");
        var font = new Option<string>("--font-file") { Description = "Single static TrueType font file or collection.", Required = true };
        var fontIndex = new Option<int>("--font-collection-index") { Description = "Zero-based font collection face index." };
        var sheet = new Option<string[]>("--sheet") { Description = "Worksheet names in output order (repeatable).", AllowMultipleArgumentsPerToken = true };
        var verbose = CommandSupport.VerboseOption();
        var metrics = new Option<string>("--column-width-metrics") { Description = "Column metrics: source (default), fallback (skip original-font lookup) or libreoffice.", DefaultValueFactory = _ => "source" };
        var mdw = new Option<double?>("--column-width-mdw") { Description = "Explicit Normal digit width in pixels at 96 DPI." };
        var fallbackMdw = new Option<double?>("--column-width-fallback-mdw") { Description = "Normal digit width at 96 DPI used only if the original font is unavailable; overrides the fallback policy." };
        var fallbackMetrics = new Option<string>("--column-width-fallback-metrics") { Description = "Missing-source metrics: fixed (default, 8px) or font (measure the drawing substitute).", DefaultValueFactory = _ => "fixed" };
        var layoutFont = new Option<string?>("--layout-font") { Description = "Original font file used only for column measurements." };
        var layoutFontIndex = new Option<int?>("--layout-font-index") { Description = "Zero-based measurement face index; requires --layout-font." };
        var noSystemFonts = new Option<bool>("--no-system-fonts") { Description = "Disable system lookup of the original Normal font for column measurements." };
        var data = MappingCommandSupport.DataOption();
        var command = new Command("core", "Convert Excel to PDF using ExcelRenderer.Core and one supplied font (saved values only).")
        {
            input, output, font, fontIndex, sheet, verbose, data, metrics, mdw, fallbackMdw, fallbackMetrics, layoutFont, layoutFontIndex, noSystemFonts,
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
                    FontFilePath = result.GetValue(font)!,
                    FontCollectionIndex = result.GetValue(fontIndex),
                    SheetNames = CommandSupport.GetSheetNames(result.GetValue(sheet)),
                },
                result.GetValue(verbose),
                cancellationToken),
            cancellationToken)));
        return command;
    }

    private static async Task ConvertAsync(string inputPath, string outputPath, Core.PdfExportOptions options, bool verbose, CancellationToken cancellationToken)
    {
        outputPath = Path.GetFullPath(outputPath);
        if (File.Exists(outputPath))
        {
            throw new IOException($"Output file already exists: {outputPath}");
        }

        await using var input = File.OpenRead(inputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var temporary = outputPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            Core.ConversionResult conversion;
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                conversion = await Core.ExcelConverter.ConvertAsync(input, output, options, cancellationToken).ConfigureAwait(false);
            }

            foreach (var diagnostic in conversion.Diagnostics)
            {
                var resolved = diagnostic.Code == "MaximumDigitWidthResolved";
                if (!resolved || verbose)
                {
                    Console.Error.WriteLine($"{(resolved ? "Info" : "Warning")}: {diagnostic.Code} [{diagnostic.SheetName ?? "workbook"}, page {diagnostic.PageNumber}]: {diagnostic.Message}");
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, outputPath);
            if (verbose)
            {
                Console.Error.WriteLine($"Core PDF: {conversion.PageCount} pages; {conversion.Diagnostics.Count} diagnostics; output: {outputPath}");
            }
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
