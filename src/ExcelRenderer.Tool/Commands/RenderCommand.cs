using System.CommandLine;
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
        var strict = new Option<bool>("--strict") { Description = "Treat all warnings and errors as conversion failures." };
        var warningsAsErrors = new Option<string[]>("--warnings-as-errors")
        {
            Description = "Diagnostic code to treat as an error.",
            AllowMultipleArgumentsPerToken = true,
        };
        var manifest = new Option<string?>("--manifest") { Description = "Optional path for the conversion manifest JSON." };
        var fontPolicy = new Option<string>("--font-policy")
        {
            Description = "Font policy: bundled or requested.",
            DefaultValueFactory = _ => "bundled",
        };
        fontPolicy.Validators.Add(result =>
        {
            var value = result.GetValueOrDefault<string>();
            if (value is not ("bundled" or "requested"))
            {
                result.AddError("--font-policy must be bundled or requested.");
            }
        });
        var fontDirectories = new Option<string[]>("--font-dir")
        {
            Description = "Additional font directory.",
            AllowMultipleArgumentsPerToken = true,
        };
        var fallbackFonts = new Option<string[]>("--fallback-font")
        {
            Description = "Fallback font family.",
            AllowMultipleArgumentsPerToken = true,
        };
        var noSystemFonts = new Option<bool>("--no-system-fonts") { Description = "Do not search operating-system fonts." };
        var command = new Command("render", "Render an Excel workbook using the unified rendering API.")
        {
            input, output, format, sheet, pages, imageLayout, strict, warningsAsErrors, manifest, fontPolicy, fontDirectories, fallbackFonts, noSystemFonts,
        };

        command.SetAction((result, cancellationToken) => CommandSupport.RunAsync(() => RenderAsync(
            result.GetValue(input)!,
            result.GetValue(output)!,
            result.GetValue(format)!,
            result.GetValue(sheet),
            result.GetValue(pages),
            result.GetValue(imageLayout)!,
            result.GetValue(strict),
            result.GetValue(warningsAsErrors),
            result.GetValue(manifest),
            result.GetValue(fontPolicy)!,
            result.GetValue(fontDirectories),
            result.GetValue(fallbackFonts),
            result.GetValue(noSystemFonts),
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
        bool strict,
        string[]? warningsAsErrors,
        string? manifestPath,
        string fontPolicy,
        string[]? fontDirectories,
        string[]? fallbackFonts,
        bool noSystemFonts,
        CancellationToken cancellationToken)
    {
        if (!TryParseFormat(formatText, out var format) || !TryParsePages(pagesText, out var pages) ||
            !TryParseImageLayout(imageLayoutText, out var imageLayout))
        {
            throw new ArgumentException("Invalid render command options.");
        }

        var request = new RenderRequest
        {
            OutputFormat = format,
            ImageLayout = imageLayout,
            Selection = new SelectionOptions
            {
                SheetNames = sheetNames is { Length: > 0 } ? sheetNames : null,
                Pages = pages,
            },
            DiagnosticOptions = new DiagnosticOptions
            {
                StrictMode = strict,
                TreatAsErrors = warningsAsErrors is { Length: > 0 } ? warningsAsErrors : Array.Empty<string>(),
            },
            FontOptions = new FontOptions
            {
                Policy = fontPolicy == "requested" ? FontPolicy.PreferRequested : FontPolicy.BundledCompatible,
                AllowSystemFonts = !noSystemFonts,
                FontDirectories = fontDirectories is { Length: > 0 } ? fontDirectories : Array.Empty<string>(),
                FallbackFamilies = fallbackFonts is { Length: > 0 } ? fallbackFonts : new FontOptions().FallbackFamilies,
            },
        };

        await using var input = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        ConversionResult conversion;
        if (format == OutputFormat.Pdf)
        {
            EnsureParentDirectory(outputPath);
            await using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
            conversion = await ExcelConverter.RenderAsync(input, request, new SingleStreamOutputSink(output), cancellationToken).ConfigureAwait(false);
        }
        else
        {
            conversion = await ExcelConverter.RenderAsync(input, request, new DirectoryOutputSink(outputPath), cancellationToken).ConfigureAwait(false);
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
