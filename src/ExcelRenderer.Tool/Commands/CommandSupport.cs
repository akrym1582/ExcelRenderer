using System.CommandLine;
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
            Description = "TrueType/OpenType font file to register (repeatable; .ttf, .tte, and supported .otf contents are accepted).",
            AllowMultipleArgumentsPerToken = true,
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
        return new(fontDirectories, fontFiles, fallbackFonts, noSystemFonts, fontPolicy, ivsFontStyle);
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
        command.Options.Add(options.FontDirectories);
        command.Options.Add(options.FontFiles);
        command.Options.Add(options.FallbackFonts);
        command.Options.Add(options.NoSystemFonts);
        command.Options.Add(options.FontPolicy);
        command.Options.Add(options.IvsFontStyle);
    }

    /// <summary>
    /// 変換処理を実行し、キャンセルまたは例外を標準エラーへ通知して終了コードへ変換します。
    /// </summary>
    /// <param name="conversion">実行する非同期の変換処理。</param>
    /// <returns>正常に完了した場合は <c>0</c>、キャンセルまたは例外が発生した場合は <c>1</c>。</returns>
    internal static async Task<int> RunAsync(Func<Task> conversion)
    {
        try
        {
            await conversion().ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Conversion was cancelled.");
            return 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Error: {exception.Message}");
            if (exception is ExcelRenderer.Rendering.ConversionException && exception.InnerException is { } cause)
            {
                Console.Error.WriteLine(cause.Message);
            }

            return 1;
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

    private static IReadOnlyList<string> NonEmpty(string[]? values) => values is { Length: > 0 } ? values : Array.Empty<string>();

    /// <summary>System.CommandLine のフォントオプション一式を保持します。</summary>
    internal sealed record FontOptionsArguments(
        Option<string[]> FontDirectories,
        Option<string[]> FontFiles,
        Option<string[]> FallbackFonts,
        Option<bool> NoSystemFonts,
        Option<string> FontPolicy,
        Option<string> IvsFontStyle);
}
