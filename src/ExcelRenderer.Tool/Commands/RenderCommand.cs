using System.CommandLine;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using ExcelRenderer;
using ExcelRenderer.Fonts;
using ExcelRenderer.Rendering;

namespace ExcelRenderer.Tool.Commands;

/// <summary>Builds the unified rendering command backed by <see cref="ExcelConverter.RenderAsync"/>.</summary>
public static class RenderCommand
{
    /// <summary>Creates the <c>render</c> command.</summary>
    /// <returns>A command that renders a workbook using the requested output format.</returns>
    public static Command Create()
    {
        var input = CommandSupport.InputArgument();
        var output = CommandSupport.OutputOption("Output file for PDF, or output directory for other formats.");
        var format = new Option<string>("--format") { Description = "Output format: pdf, png, svg, or markdown.", Required = true };
        format.Validators.Add(result =>
        {
            if (!TryParseFormat(result.GetValueOrDefault<string>(), out _))
            {
                result.AddError("--format must be one of: pdf, png, svg, markdown.");
            }
        });
        var sheet = new Option<string[]>("--sheet") { Description = "Worksheet name to render.", AllowMultipleArgumentsPerToken = true };
        var pages = new Option<string?>("--pages") { Description = "Document pages to render, for example: 1,3-5." };
        pages.Validators.Add(result =>
        {
            var value = result.GetValueOrDefault<string>();
            if (value is not null && !TryParsePages(value, out _))
            {
                result.AddError("--pages must use positive page numbers and ranges, for example: 1,3-5.");
            }
        });
        var imageLayout = new Option<string>("--image-layout")
        {
            Description = "Image layout for PNG/SVG: paginated or continuous.",
            DefaultValueFactory = _ => "paginated",
        };
        imageLayout.Validators.Add(result =>
        {
            if (result.GetValueOrDefault<string>() is not ("paginated" or "continuous"))
            {
                result.AddError("--image-layout must be paginated or continuous.");
            }
        });
        var ranges = new Option<string[]>("--range") { Description = "Explicit A1 rectangle, optionally sheet-qualified. Repeat once per sheet." };
        var maxRangeCells = new Option<long>("--max-range-cells") { Description = "Maximum total explicit range cells.", DefaultValueFactory = _ => 1_000_000 };
        var trim = new Option<bool>("--trim") { Description = "Crop each page or canvas around visible drawing content." };
        var padding = new Option<double?>("--trim-padding") { Description = "Nonnegative crop padding in points (default: 2). Requires --trim." };
        var hyperlinks = CommandSupport.HyperlinksOption();
        var strict = new Option<bool>("--strict") { Description = "Treat all warnings and errors as conversion failures." };
        var warningsAsErrors = new Option<string[]>("--warnings-as-errors")
        {
            Description = "Diagnostic code to treat as an error.",
            AllowMultipleArgumentsPerToken = true,
        };
        var manifest = new Option<string?>("--manifest") { Description = "Optional path for the conversion manifest JSON." };
        var fonts = CommandSupport.FontOptions();
        var command = new Command("render", "Render an Excel workbook using the unified rendering API.")
        {
            input, output, format, sheet, pages, imageLayout, ranges, maxRangeCells, trim, padding, hyperlinks, strict, warningsAsErrors, manifest,
        };
        CommandSupport.AddFontOptions(command, fonts);

        command.SetAction((result, cancellationToken) => CommandSupport.RunAsync(() => RenderAsync(
            result.GetValue(input)!,
            result.GetValue(output)!,
            result.GetValue(format)!,
            result.GetValue(sheet),
            result.GetValue(pages),
            result.GetValue(imageLayout)!,
            result.GetValue(ranges),
            result.GetValue(maxRangeCells),
            result.GetValue(trim),
            result.GetValue(padding),
            CommandSupport.GetHyperlinks(result.GetValue(hyperlinks)!),
            result.GetValue(strict),
            result.GetValue(warningsAsErrors),
            result.GetValue(manifest),
            CommandSupport.GetFontOptions(result, fonts),
            cancellationToken)));
        return command;
    }

    private static async Task RenderAsync(
        string inputPath,
        string outputPath,
        string formatText,
        string[]? sheetNames,
        string? pagesText,
        string imageLayoutText,
        string[]? rangeTexts,
        long maxRangeCells,
        bool trim,
        double? padding,
        HyperlinkMode hyperlinks,
        bool strict,
        string[]? warningsAsErrors,
        string? manifestPath,
        FontOptions fontOptions,
        CancellationToken cancellationToken)
    {
        if (!TryParseFormat(formatText, out var format) || !TryParsePages(pagesText, out var pages) ||
            !TryParseImageLayout(imageLayoutText, out var imageLayout))
        {
            throw new ArgumentException("Invalid render command options.");
        }

        if (Path.GetFullPath(inputPath).Equals(Path.GetFullPath(outputPath), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new ArgumentException("Input and output paths must differ.");
        }

        if (padding is not null && !trim)
        {
            throw new ArgumentException("--trim-padding requires --trim.");
        }

        IReadOnlyList<SheetRangeSelection>? ranges = null;
        if (rangeTexts is { Length: > 0 })
        {
            var parsed = rangeTexts.Select(text =>
            {
                var range = CellRangeParser.Parse(text, out var name);
                return (Range: range, Name: name);
            }).ToArray();
            string[] selectedNames;
            if (sheetNames is { Length: > 0 })
            {
                selectedNames = sheetNames.Distinct(StringComparer.Ordinal).ToArray();
            }
            else
            {
                using var workbook = SpreadsheetDocument.Open(inputPath, false);
                selectedNames = workbook.WorkbookPart!.Workbook.Sheets!.Elements<Sheet>().Select(s => s.Name!.Value!).ToArray();
            }

            ranges = parsed.Select(item => new SheetRangeSelection(
                item.Name ?? (selectedNames.Length == 1 ? selectedNames[0] :
                throw new ArgumentException("An unqualified --range requires exactly one selected sheet.")),
                item.Range)).ToArray();
        }

        var request = new RenderRequest
        {
            OutputFormat = format,
            Trim = new TrimOptions { Enabled = trim, PaddingPoints = padding ?? 2 },
            Hyperlinks = hyperlinks,
            ImageLayout = imageLayout,
            Selection = new SelectionOptions
            {
                SheetNames = sheetNames is { Length: > 0 } ? sheetNames : null,
                Pages = pages,
                Ranges = ranges,
                MaxRangeCells = maxRangeCells,
            },
            DiagnosticOptions = new DiagnosticOptions
            {
                StrictMode = strict,
                TreatAsErrors = warningsAsErrors is { Length: > 0 } ? warningsAsErrors : Array.Empty<string>(),
            },
            FontOptions = fontOptions,
        };

        await using var input = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        ConversionResult conversion;
        if (format == OutputFormat.Pdf)
        {
            conversion = await ExcelConverter.RenderAsync(input, request, new FileOutputSink(outputPath), cancellationToken).ConfigureAwait(false);
        }
        else
        {
            conversion = await ExcelConverter.RenderAsync(input, request, new DirectoryOutputSink(outputPath), cancellationToken).ConfigureAwait(false);
        }

        foreach (var diagnostic in conversion.Diagnostics.Where(x => x.Severity != DiagnosticSeverity.Info))
        {
            var location = diagnostic.CellRange ?? diagnostic.ObjectId ?? "workbook";
            Console.Error.WriteLine($"{diagnostic.Severity}: {diagnostic.Code} [{diagnostic.SheetName ?? "workbook"}!{location}]: {diagnostic.Message}");
        }

        if (manifestPath is not null)
        {
            EnsureParentDirectory(manifestPath);
            await using var manifest = new FileStream(manifestPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await ConversionManifest.WriteAsync(manifest, conversion, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool TryParseFormat(string? text, out OutputFormat format)
    {
        format = text?.ToLowerInvariant() switch
        {
            "pdf" => OutputFormat.Pdf,
            "png" => OutputFormat.Png,
            "svg" => OutputFormat.Svg,
            "markdown" => OutputFormat.Markdown,
            _ => default,
        };
        return text is not null && (text.Equals("pdf", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("png", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("svg", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("markdown", StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryParseImageLayout(string? text, out ImageLayoutMode layout)
    {
        layout = text?.ToLowerInvariant() switch
        {
            "paginated" => ImageLayoutMode.Paginated,
            "continuous" => ImageLayoutMode.Continuous,
            _ => default,
        };
        return text is not null && (text.Equals("paginated", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("continuous", StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryParsePages(string? text, out IReadOnlyList<int>? pages)
    {
        pages = null;
        if (text is null)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parsed = new List<int>();
        foreach (var part in text.Split(','))
        {
            var bounds = part.Split('-');
            if (bounds.Length is < 1 or > 2 ||
                !int.TryParse(bounds[0], out var start) || start <= 0)
            {
                return false;
            }

            var end = start;
            if (bounds.Length == 2 && (!int.TryParse(bounds[1], out end) || end < start))
            {
                return false;
            }

            for (var page = start; ; page++)
            {
                parsed.Add(page);
                if (page == end)
                {
                    break;
                }
            }
        }

        pages = parsed;
        return true;
    }

    private static void EnsureParentDirectory(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }
    }
}
