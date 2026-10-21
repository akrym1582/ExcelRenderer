using System.CommandLine;
using System.Diagnostics;
using ExcelRenderer.Fonts;

namespace ExcelRenderer.Tool.Commands;

/// <summary>
/// 各変換コマンドで共通して使用する引数、オプション、および終了コード処理を提供します。
/// </summary>
internal static class CommandSupport
{
    /// <summary>
    /// 変換元となる Excel ファイルのパスを受け取る位置引数を作成します。
    /// </summary>
    /// <returns><c>input</c> という名前を持つ必須の文字列引数。</returns>
    internal static Argument<string> InputArgument() => new("input")
    {
        Description = "Path to the input .xlsx file.",
    };

    /// <summary>
    /// 変換結果の出力先を受け取る必須オプションを作成します。
    /// </summary>
    /// <param name="description">ヘルプに表示する出力先の説明。</param>
    /// <returns><c>--output</c> および <c>-o</c> で指定できる必須の文字列オプション。</returns>
    internal static Option<string> OutputOption(string description)
    {
        var option = new Option<string>("--output") { Description = description, Required = true };
        option.Aliases.Add("-o");
        return option;
    }

    /// <summary>すべての変換コマンドで共有するフォント検索オプションを作成します。</summary>
    /// <returns>生成したフォントオプション一式。</returns>
    internal static FontOptionsArguments FontOptions()
    {
        var fontDirectories = new Option<string[]>("--font-dir")
        {
            Description = "Additional font directory (repeatable).",
            AllowMultipleArgumentsPerToken = true,
        };
        var fontFiles = new Option<string[]>("--font-file")
        {
            Description = "Font file or TTC/OTC collection to register (repeatable; contents determine format support).",
            AllowMultipleArgumentsPerToken = true,
        };
        var faceIndex = new Option<int?>("--font-face-index")
        {
            Description = "Zero-based collection face index; requires exactly one --font-file. Omit to enumerate all faces.",
        };
        var fallbackFonts = new Option<string[]>("--fallback-font")
        {
            Description = "Fallback font family, in priority order (repeatable).",
            AllowMultipleArgumentsPerToken = true,
        };
        var noSystemFonts = new Option<bool>("--no-system-fonts") { Description = "Do not search operating-system fonts." };
        var fontPolicy = new Option<string>("--font-policy")
        {
            Description = "Font policy: bundled or requested.",
            DefaultValueFactory = _ => "bundled",
        };
        fontPolicy.Validators.Add(result =>
        {
            if (result.GetValueOrDefault<string>() is not ("bundled" or "requested"))
            {
                result.AddError("--font-policy must be bundled or requested.");
            }
        });
        var ivsFontStyle = new Option<string>("--ivs-font-style")
        {
            Description = "Bundled IVS font style: gothic or mincho.",
            DefaultValueFactory = _ => "gothic",
        };
        ivsFontStyle.Validators.Add(result =>
        {
            if (result.GetValueOrDefault<string>() is not ("gothic" or "mincho"))
            {
                result.AddError("--ivs-font-style must be gothic or mincho.");
            }
        });
        return new(fontDirectories, fontFiles, fallbackFonts, noSystemFonts, fontPolicy, ivsFontStyle, faceIndex);
    }

    /// <summary>コマンドラインで指定されたフォント検索設定をライブラリ設定へ変換します。</summary>
    /// <param name="result">解析済みのコマンドライン。</param>
    /// <param name="options">値を取得するフォントオプション。</param>
    /// <returns>ライブラリへ渡すフォント設定。</returns>
    internal static FontOptions GetFontOptions(ParseResult result, FontOptionsArguments options) => new()
    {
        AllowSystemFonts = !result.GetValue(options.NoSystemFonts),
        FontDirectories = NonEmpty(result.GetValue(options.FontDirectories)),
        FontFiles = NonEmpty(result.GetValue(options.FontFiles)),
        FontFileCollectionIndex = result.GetValue(options.FontFaceIndex),
        FallbackFamilies = result.GetValue(options.FallbackFonts) is { Length: > 0 } fallbacks
            ? fallbacks
            : new FontOptions().FallbackFamilies,
        Policy = result.GetValue(options.FontPolicy) == "requested" ? FontPolicy.PreferRequested : FontPolicy.BundledCompatible,
        IvsFontStyle = result.GetValue(options.IvsFontStyle) == "mincho" ? IvsFontStyle.Mincho : IvsFontStyle.Gothic,
    };

    /// <summary>フォント検索オプションをコマンドへ追加します。</summary>
    /// <param name="command">オプションを追加するコマンド。</param>
    /// <param name="options">追加するフォントオプション。</param>
    internal static void AddFontOptions(Command command, FontOptionsArguments options)
    {
        command.Options.Add(options.ColumnWidthMdw);
        command.Options.Add(options.LayoutFont);
        command.Options.Add(options.LayoutFontIndex);
        command.Options.Add(options.ColumnWidthMetrics);
        command.Options.Add(options.FontDirectories);
        command.Options.Add(options.FontFiles);
        command.Options.Add(options.FallbackFonts);
        command.Options.Add(options.NoSystemFonts);
        command.Options.Add(options.FontPolicy);
        command.Options.Add(options.IvsFontStyle);
        command.Options.Add(options.FontFaceIndex);
        command.Validators.Add(result =>
        {
            if (result.GetValue(options.FontFaceIndex) is { } index &&
                (index < 0 || result.GetValue(options.FontFiles)?.Length != 1))
            {
                result.AddError("--font-face-index requires exactly one --font-file and a nonnegative index.");
            }
        });
    }

    /// <summary>
    /// 変換処理を実行し、キャンセルまたは例外を標準エラーへ通知して終了コードへ変換します。
    /// </summary>
    /// <param name="result">The parsed command and verbosity option.</param>
    /// <param name="conversion">実行する非同期の変換処理。</param>
    /// <returns>正常に完了した場合は <c>0</c>、キャンセルまたは例外が発生した場合は <c>1</c>。</returns>
    internal static async Task<int> RunAsync(ParseResult result, Func<Task> conversion)
    {
        var verbose = result.GetValue<bool>("--verbose");
        var timer = Stopwatch.StartNew();
        var previousObserver = Core.Rendering.ConversionMetrics.Observer;
        var metrics = new Dictionary<string, double>(StringComparer.Ordinal);
        if (verbose)
        {
            Core.Rendering.ConversionMetrics.Observer = (name, value) =>
            {
                metrics[name] = name.EndsWith(".ms", StringComparison.Ordinal) ? metrics.GetValueOrDefault(name) + value : value;
                previousObserver?.Invoke(name, value);
            };
            Console.Error.WriteLine($"Starting {result.CommandResult.Command.Name}: {string.Join(" ", result.Tokens.Select(token => token.Value))}");
        }

        try
        {
            await conversion().ConfigureAwait(false);
            if (verbose)
            {
                Console.Error.WriteLine($"Completed in {timer.Elapsed.TotalMilliseconds:F1} ms.");
            }

            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Conversion was cancelled.");
            return 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(verbose ? exception.ToString() : $"Error: {exception.Message}");
            if (exception is ExcelRenderer.Rendering.ConversionException && exception.InnerException is { } cause)
            {
                Console.Error.WriteLine(cause.Message);
            }

            return 1;
        }
        finally
        {
            Core.Rendering.ConversionMetrics.Observer = previousObserver;
            if (verbose)
            {
                foreach (var metric in metrics.OrderBy(item => item.Key, StringComparer.Ordinal))
                {
                    Console.Error.WriteLine($"Metric {metric.Key}: {metric.Value:F3}");
                }
            }
        }
    }

    /// <summary>Creates the shared hyperlink preservation option.</summary>
    /// <returns>The validated CLI option.</returns>
    internal static Option<string> HyperlinksOption()
    {
        var option = new Option<string>("--hyperlinks") { Description = "Cell hyperlinks: preserve or none.", DefaultValueFactory = _ => "preserve" };
        option.Validators.Add(result =>
        {
            if (result.GetValueOrDefault<string>() is not ("preserve" or "none"))
            {
                result.AddError("--hyperlinks must be preserve or none.");
            }
        });
        return option;
    }

    /// <summary>Reads the shared hyperlink mode.</summary>
    /// <param name="value">The validated option text.</param>
    /// <returns>The requested mode.</returns>
    internal static ExcelRenderer.Rendering.HyperlinkMode GetHyperlinks(string value) => value switch
    {
        "preserve" => ExcelRenderer.Rendering.HyperlinkMode.Preserve,
        "none" => ExcelRenderer.Rendering.HyperlinkMode.None,
        _ => throw new ArgumentException("--hyperlinks must be preserve or none."),
    };

    /// <summary>Creates the PDF drawing backend option.</summary>
    /// <returns>The validated option.</returns>
    internal static Option<string> PdfBackendOption()
    {
        var option = new Option<string>("--pdf-backend")
        {
            Description = "PDF backend: skiasharp (default).",
            DefaultValueFactory = _ => "skiasharp",
        };
        option.Validators.Add(result =>
        {
            if (result.GetValueOrDefault<string>() is not "skiasharp")
            {
                result.AddError("PDFsharp has been removed. Use --pdf-backend skiasharp.");
            }
        });
        return option;
    }

    /// <summary>Maps a validated backend option.</summary>
    /// <param name="value">The validated option value.</param>
    /// <returns>The drawing backend.</returns>
    internal static PdfBackend GetPdfBackend(string value) => value switch
    {
        "skiasharp" => PdfBackend.SkiaSharp,
        _ => throw new ArgumentException("Unknown PDF backend.", nameof(value)),
    };

    /// <summary>Creates the shared formula display option.</summary>
    /// <returns>The validated mode option.</returns>
    internal static Option<string> FormulaModeOption()
    {
        var option = new Option<string>("--formula-mode") { Description = "Formula display: cached (saved results) or recalculate (limited built-in engine).", DefaultValueFactory = _ => "cached" };
        option.Validators.Add(result =>
        {
            if (result.GetValueOrDefault<string>() is "legacy")
            {
                result.AddError("Legacy mode is unsupported. Use --formula-mode cached or recalculate.");
            }
            else if (result.GetValueOrDefault<string>() is not "cached" and not "recalculate")
            {
                result.AddError("--formula-mode must be cached or recalculate.");
            }
        });
        return option;
    }

    /// <summary>Maps a validated mode to workbook read settings.</summary>
    /// <param name="value">The mode text.</param>
    /// <param name="result">Parsed command values.</param>
    /// <param name="fonts">Shared font and layout metric options.</param>
    /// <returns>The requested read policy.</returns>
    internal static Excel.ExcelReadOptions GetReadOptions(string value, ParseResult result, FontOptionsArguments fonts) => new()
    {
        ColumnWidthMetrics = new()
        {
            Mode = result.GetValue(fonts.ColumnWidthMetrics) switch
            {
                "source" => Excel.ColumnWidthMetricsMode.SourceFont,
                "legacy" => Excel.ColumnWidthMetricsMode.RenderingFontLegacy,
                _ => throw new ArgumentException("--column-width-metrics must be source or legacy."),
            },
            MaximumDigitWidthPixels96Dpi = result.GetValue(fonts.ColumnWidthMdw),
            SourceFontFile = result.GetValue(fonts.LayoutFont),
            SourceFontCollectionIndex = result.GetValue(fonts.LayoutFontIndex),
        },
        FormulaDisplayMode = value switch
        {
            "cached" => Excel.FormulaDisplayMode.CachedValue,
            "recalculate" => Excel.FormulaDisplayMode.Recalculate,
            _ => throw new ArgumentException("Invalid formula mode."),
        },
    };

    /// <summary>Maps a validated formula mode to template mapping settings.</summary>
    /// <param name="value">The mode text.</param>
    /// <param name="recalculateMappedWorkbook">Whether to calculate all saved XLSX results before returning.</param>
    /// <returns>The requested calculation policy.</returns>
    internal static Mapping.Configuration.MappingOptions GetMappingOptions(string value, bool recalculateMappedWorkbook = false) => new()
    {
        FormulaCalculationMode = value switch
        {
            "cached" => OpenXml.Calculation.FormulaCalculationMode.CachedValue,
            "recalculate" => OpenXml.Calculation.FormulaCalculationMode.Recalculate,
            _ => throw new ArgumentException("Invalid formula mode."),
        },
        RecalculateMappedWorkbook = recalculateMappedWorkbook,
    };

    /// <summary>Creates the shared detailed logging option.</summary>
    /// <returns>The verbosity flag.</returns>
    internal static Option<bool> VerboseOption() => new("--verbose") { Description = "Write command details, elapsed time, conversion metrics, informational diagnostics and full exceptions to standard error." };

    /// <summary>Normalizes an omitted worksheet option to all worksheets.</summary>
    /// <param name="names">The parsed names.</param>
    /// <returns>The selection or null.</returns>
    internal static IReadOnlyList<string>? GetSheetNames(string[]? names) => names is { Length: > 0 } ? names : null;

    private static IReadOnlyList<string> NonEmpty(string[]? values) => values is { Length: > 0 } ? values : Array.Empty<string>();

    /// <summary>System.CommandLine のフォントオプション一式を保持します。</summary>
    internal sealed record FontOptionsArguments(
        Option<string[]> FontDirectories,
        Option<string[]> FontFiles,
        Option<string[]> FallbackFonts,
        Option<bool> NoSystemFonts,
        Option<string> FontPolicy,
        Option<string> IvsFontStyle,
        Option<int?> FontFaceIndex)
    {
        /// <summary>Gets explicit Normal digit width.</summary>
        internal Option<double?> ColumnWidthMdw { get; } = new("--column-width-mdw") { Description = "Original Normal font MDW in pixels at 96 DPI (finite, at least 1)." };

        /// <summary>Gets the source layout font file.</summary>
        internal Option<string?> LayoutFont { get; } = new("--layout-font") { Description = "Original font file for column layout only; does not register a drawing font." };

        /// <summary>Gets the source layout face index.</summary>
        internal Option<int?> LayoutFontIndex { get; } = new("--layout-font-index") { Description = "Zero-based face index; requires --layout-font." };

        /// <summary>Gets the column metric policy.</summary>
        internal Option<string> ColumnWidthMetrics { get; } = new("--column-width-metrics") { Description = "Column metrics: source (default) or legacy.", DefaultValueFactory = _ => "source" };
    }
}
