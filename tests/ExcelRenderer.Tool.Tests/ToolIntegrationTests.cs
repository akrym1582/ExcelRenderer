using System.Diagnostics;
using System.Text;
using Xunit;

namespace ExcelRenderer.Tool.Tests;

/// <summary>
/// コマンドラインツールの各変換コマンド、入力検証およびエラー表示を検証します。
/// </summary>
public sealed partial class ToolIntegrationTests : IDisposable
{
    /// <summary>コマンドテストごとに作成する、一意の一時出力ディレクトリです。</summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ExcelRenderer.Tool.Tests", Guid.NewGuid().ToString("N"));

    /// <summary>コマンド変換の入力に使用する同梱サンプル XLSX のパスです。</summary>
    private static string Input => Path.Combine(AppContext.BaseDirectory, "SampleInputs", "sample.xlsx");

    /// <summary>
    /// テストごとに作成した一時ディレクトリと出力ファイルを削除します。
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    /// <summary>
    /// PDF コマンドが Excel 入力から PDF ファイルを生成することを検証します。
    /// </summary>
    /// <returns>非同期の検証処理を表すタスク。</returns>
    [Fact(DisplayName = "PDF コマンドが Excel 入力から PDF ファイルを生成する")]
    public async Task Pdf_command_creates_a_pdf()
    {
        var output = Path.Combine(_directory, "report.pdf");
        var result = await RunAsync("pdf", Input, "-o", output);
        AssertSuccess(result);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(File.ReadAllBytes(output), 0, 4));
    }

    /// <summary>
    /// image コマンドが Excel 入力からページ単位の PNG ファイルを生成することを検証します。
    /// </summary>
    /// <returns>非同期の検証処理を表すタスク。</returns>
    [Fact(DisplayName = "image コマンドが Excel 入力からページ単位の PNG ファイルを生成する")]
    public async Task Image_command_creates_png_files()
    {
        var output = Path.Combine(_directory, "images");
        var result = await RunAsync("image", Input, "-o", output, "--dpi", "72");
        AssertSuccess(result);
        var png = Assert.Single(Directory.GetFiles(output, "*.png"));
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4e, 0x47 }, File.ReadAllBytes(png)[..4]);
    }

    /// <summary>svg と render の両コマンドでバッファ上限を指定でき、一時ファイル禁止時の失敗後も退避ファイルが残らないことを検証します。</summary>
    /// <param name="command">検証する SVG 出力コマンド（svg または render）。</param>
    /// <returns>コマンド実行と出力・一時ファイルの検証が完了するまでの非同期処理。</returns>
    [Theory(DisplayName = "svg と render の両コマンドでバッファ上限を指定でき、一時ファイル禁止時の失敗後も退避ファイルが残らない")]
    [InlineData("svg")]
    [InlineData("render")]
    public async Task Svg_buffer_options_support_spill_and_no_temp_failure(string command)
    {
        Directory.CreateDirectory(_directory);
        var temporary = Path.Combine(_directory, "buffer-temp");
        Directory.CreateDirectory(temporary);
        var output = Path.Combine(_directory, "buffer-svg");
        var arguments = new List<string> { command, Input, "-o", output, "--buffer-memory-threshold", "1", "--buffer-temp-directory", temporary };
        if (command == "render")
        {
            arguments.AddRange(["--format", "svg"]);
        }

        AssertSuccess(await RunAsync(arguments.ToArray()));
        Assert.Empty(Directory.GetFiles(temporary));
        arguments[3] = Path.Combine(_directory, "rejected-svg");
        arguments.Add("--no-buffer-temp");
        var rejected = await RunAsync(arguments.ToArray());
        Assert.NotEqual(0, rejected.ExitCode);
        Assert.Contains("MemoryThresholdBytes", rejected.Error);
        Assert.Empty(Directory.GetFiles(temporary));
    }

    /// <summary>svg コマンドが Excel 入力からページ単位の SVG ファイルを生成することを検証します。</summary>
    /// <returns>非同期の検証処理を表すタスク。</returns>
    [Fact(DisplayName = "svg コマンドが Excel 入力からページ単位の SVG ファイルを生成する")]
    public async Task Svg_command_creates_svg_files()
    {
        var output = Path.Combine(_directory, "svg");
        var result = await RunAsync("svg", Input, "-o", output);
        AssertSuccess(result);
        var svg = Assert.Single(Directory.GetFiles(output, "*.svg"));
        Assert.Contains("<svg", await File.ReadAllTextAsync(svg));
    }

    /// <summary>svg コマンドが PNG 専用の DPI オプションを受け付けないことを検証します。</summary>
    /// <returns>非同期の検証処理を表すタスク。</returns>
    [Fact(DisplayName = "svg コマンドが PNG 専用の DPI オプションを受け付けない")]
    public async Task Svg_command_rejects_dpi()
    {
        var result = await RunAsync("svg", Input, "-o", Path.Combine(_directory, "svg"), "--dpi", "72");
        Assert.NotEqual(0, result.ExitCode);
    }

    /// <summary>
    /// md エイリアスが Excel 入力から Markdown ファイルを生成することを検証します。
    /// </summary>
    /// <returns>非同期の検証処理を表すタスク。</returns>
    [Fact(DisplayName = "md エイリアスが Excel 入力から Markdown ファイルを生成する")]
    public async Task Md_alias_creates_markdown()
    {
        var output = Path.Combine(_directory, "nested", "report.md");
        var result = await RunAsync("md", Input, "-o", output);
        AssertSuccess(result);
        Assert.Contains("折り返し", await File.ReadAllTextAsync(output));
    }

    /// <summary>render コマンドが RenderAsync 経由で PDF とマニフェストを生成することを検証します。</summary>
    /// <returns>非同期の検証処理を表すタスク。</returns>
    [Fact(DisplayName = "render コマンドが RenderAsync 経由で PDF とマニフェストを生成する")]
    public async Task Render_command_creates_pdf_and_manifest()
    {
        var output = Path.Combine(_directory, "rendered", "report.pdf");
        var manifest = Path.Combine(_directory, "rendered", "manifest.json");
        var result = await RunAsync("render", Input, "-o", output, "--format", "pdf", "--pages", "1", "--manifest", manifest);
        AssertSuccess(result);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(File.ReadAllBytes(output), 0, 4));
        Assert.Contains("\"completionStatus\":\"Completed\"", await File.ReadAllTextAsync(manifest));
    }

    /// <summary>render コマンドがページごとの形式に出力ディレクトリを使用することを検証します。</summary>
    /// <returns>非同期の検証処理を表すタスク。</returns>
    [Fact(DisplayName = "render コマンドがページごとの形式に出力ディレクトリを使用する")]
    public async Task Render_command_creates_svg_in_output_directory()
    {
        var output = Path.Combine(_directory, "rendered-svg");
        var result = await RunAsync("render", Input, "-o", output, "--format", "svg", "--sheet", "折り返し");
        AssertSuccess(result);
        Assert.Single(Directory.GetFiles(output, "*.svg"));
    }

    /// <summary>render コマンドが TTE 拡張子の有効な外部フォントを受け付けます。</summary>
    /// <returns>変換処理と出力結果の検証が完了するまでの非同期処理。</returns>
    [Fact(DisplayName = "render コマンドが TTE 拡張子の有効な外部フォントを受け付けます")]
    public async Task Render_command_accepts_explicit_tte_font_file()
    {
        Directory.CreateDirectory(_directory);
        var font = Path.Combine(_directory, "external.tte");
        File.Copy(Path.Combine(FindRepositoryRoot(), "third_party", "NotoSansJP", "NotoSansJP-Regular.ttf"), font);
        var output = Path.Combine(_directory, "font-svg");

        var result = await RunAsync("render", Input, "-o", output, "--format", "svg", "--font-file", font);

        AssertSuccess(result);
        Assert.Single(Directory.GetFiles(output, "*.svg"));
    }

    /// <summary>render コマンドが存在しない外部フォントを変換前に拒否します。</summary>
    /// <returns>変換処理と出力結果の検証が完了するまでの非同期処理。</returns>
    [Fact(DisplayName = "render コマンドが存在しない外部フォントを変換前に拒否します")]
    public async Task Render_command_rejects_missing_explicit_font_file()
    {
        var result = await RunAsync("render", Input, "-o", Path.Combine(_directory, "font-svg"), "--format", "svg",
            "--font-file", Path.Combine(_directory, "missing.tte"));

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Font file was not found", result.Error);
    }

    /// <summary>すべての変換コマンドが共通フォントオプションをヘルプに公開することを検証します。</summary>
    /// <param name="command">実行するコマンド、または描画対象の文字命令。</param>
    /// <returns>変換処理と出力結果の検証が完了するまでの非同期処理。</returns>
    [Theory(DisplayName = "すべての変換コマンドが共通フォントオプションをヘルプに公開する")]
    [InlineData("pdf")]
    [InlineData("image")]
    [InlineData("svg")]
    [InlineData("markdown")]
    [InlineData("render")]
    public async Task Every_command_exposes_font_options(string command)
    {
        var result = await RunAsync(command, "--help");

        AssertSuccess(result);
        Assert.Contains("--font-dir", result.Output);
        Assert.Contains("--font-file", result.Output);
        Assert.Contains("--fallback-font", result.Output);
        Assert.Contains("--no-system-fonts", result.Output);
        Assert.Contains("--font-policy", result.Output);
        Assert.Contains("--ivs-font-style", result.Output);
    }

    /// <summary>繰り返した font-file が指定順を維持したまますべて渡されることを検証します。</summary>
    /// <returns>変換処理と出力結果の検証が完了するまでの非同期処理。</returns>
    [Fact(DisplayName = "繰り返した font-file が指定順を維持したまますべて渡される")]
    public async Task Pdf_command_preserves_multiple_font_files_in_command_line_order()
    {
        Directory.CreateDirectory(_directory);
        var missingFirst = Path.Combine(_directory, "first-missing.ttf");
        var validSecond = Path.Combine(_directory, "second-valid.ttf");
        File.Copy(Path.Combine(FindRepositoryRoot(), "third_party", "NotoSansJP", "NotoSansJP-Regular.ttf"), validSecond);

        var result = await RunAsync(
            "pdf", Input, "-o", Path.Combine(_directory, "report.pdf"),
            "--font-file", missingFirst,
            "--font-file", validSecond);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(missingFirst, result.Error);
    }

    /// <summary>従来形式の描画コマンドにも明示フォントファイルが適用されることを検証します。</summary>
    /// <param name="command">実行するコマンド、または描画対象の文字命令。</param>
    /// <returns>変換処理と出力結果の検証が完了するまでの非同期処理。</returns>
    [Theory(DisplayName = "従来形式の描画コマンドにも明示フォントファイルが適用される")]
    [InlineData("pdf")]
    [InlineData("image")]
    [InlineData("svg")]
    public async Task Format_commands_reject_a_missing_explicit_font_file(string command)
    {
        var output = command == "pdf" ? Path.Combine(_directory, "report.pdf") : Path.Combine(_directory, command);
        var result = await RunAsync(command, Input, "-o", output, "--font-file", Path.Combine(_directory, "missing.ttf"));

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Font file was not found", result.Error);
    }

    /// <summary>連続 SVG 出力がシートごとに 1 ファイルを生成することを検証します。</summary>
    /// <returns>変換処理と出力結果の検証が完了するまでの非同期処理。</returns>
    [Fact(DisplayName = "連続 SVG 出力がシートごとに 1 ファイルを生成する")]
    public async Task Render_command_creates_continuous_svg()
    {
        var output = Path.Combine(_directory, "continuous-svg");
        var result = await RunAsync("render", Input, "-o", output, "--format", "svg", "--sheet", "折り返し", "--image-layout", "continuous");

        AssertSuccess(result);
        Assert.Single(Directory.GetFiles(output, "*.svg"));
    }

    /// <summary>連続レイアウトでページ選択を指定すると説明的に失敗することを検証します。</summary>
    /// <returns>変換処理と出力結果の検証が完了するまでの非同期処理。</returns>
    [Fact(DisplayName = "連続レイアウトでページ選択を指定すると説明的に失敗する")]
    public async Task Render_command_rejects_pages_with_continuous_layout()
    {
        var result = await RunAsync("render", Input, "-o", Path.Combine(_directory, "continuous-svg"), "--format", "svg", "--pages", "1", "--image-layout", "continuous");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Page selection is not supported with continuous image layout", result.Error);
    }

    /// <summary>
    /// 存在しない入力パスまたは未対応の拡張子に対してコマンドが失敗することを検証します。
    /// </summary>
    /// <param name="arguments">入力エラーを発生させるコマンドライン引数。</param>
    /// <returns>非同期の検証処理を表すタスク。</returns>
    [Theory(DisplayName = "存在しない入力パスまたは未対応の拡張子に対してコマンドが失敗する")]
    [InlineData("md")]
    [InlineData("md", "not-found.xlsx", "-o", "output.md")]
    public async Task Invalid_input_fails(params string[] arguments) => Assert.NotEqual(0, (await RunAsync(arguments)).ExitCode);

    /// <summary>
    /// 範囲外の DPI がコマンドライン解析時に拒否されることを検証します。
    /// </summary>
    /// <returns>非同期の検証処理を表すタスク。</returns>
    [Fact(DisplayName = "範囲外の DPI がコマンドライン解析時に拒否される")]
    public async Task Invalid_dpi_is_rejected_by_parser()
    {
        var result = await RunAsync("image", Input, "-o", Path.Combine(_directory, "images"), "--dpi", "0");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("greater than zero", result.Error);
    }

    /// <summary>
    /// 存在しないシート名を指定した場合、スタックトレースを出さずエラー終了することを検証します。
    /// </summary>
    /// <returns>非同期の検証処理を表すタスク。</returns>
    [Fact(DisplayName = "存在しないシート名を指定した場合、スタックトレースを出さずエラー終了する")]
    public async Task Unknown_sheet_fails_without_a_stack_trace()
    {
        var result = await RunAsync("image", Input, "-o", Path.Combine(_directory, "images"), "--sheet", "UnknownSheet");
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Worksheet was not found", result.Error);
        Assert.DoesNotContain(" at ", result.Error);
    }

    /// <summary>render コマンドに範囲・トリミング・リンク設定を指定して事前検証を行い、PDF が生成されることを検証します。</summary>
    /// <returns>変換処理と出力結果の検証が完了するまでの非同期処理。</returns>
    [Fact(DisplayName = "render コマンドに範囲・トリミング・リンク設定を指定して事前検証を行い、PDF が生成される")]
    public async Task Render_range_trim_and_hyperlink_options_create_a_pdf_after_preflight()
    {
        var output = Path.Combine(_directory, "range.pdf");
        var result = await RunAsync("render", Input, "-o", output, "--format", "pdf", "--sheet", "折り返し",
            "--range", "A1:D8", "--trim", "--trim-padding", "2", "--hyperlinks", "none");
        AssertSuccess(result);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(File.ReadAllBytes(output), 0, 4));
    }

    /// <summary>render コマンドの引数検証が失敗した場合、既存の PDF ファイルの内容が変更されないことを検証します。</summary>
    /// <param name="option">不正値を指定して検証する CLI オプション名。</param>
    /// <param name="value">長さ変更の指定値、または CLI に渡すオプション値。</param>
    /// <returns>変換処理と出力結果の検証が完了するまでの非同期処理。</returns>
    [Theory(DisplayName = "render コマンドの引数検証が失敗した場合、既存の PDF ファイルの内容が変更されない")]
    [InlineData("--range", "B2:A1")]
    [InlineData("--trim-padding", "2")]
    [InlineData("--hyperlinks", "javascript")]
    [InlineData("--max-range-cells", "0")]
    public async Task Render_argument_failures_preserve_existing_pdf(string option, string value)
    {
        Directory.CreateDirectory(_directory);
        var output = Path.Combine(_directory, "preserved.pdf");
        File.WriteAllText(output, "sentinel");
        var result = await RunAsync("render", Input, "-o", output, "--format", "pdf", option, value);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal("sentinel", File.ReadAllText(output));
    }

    /// <summary>render コマンドで重複範囲または入力と同じ出力パスを指定すると、書き込み前に拒否されることを検証します。</summary>
    /// <returns>変換処理と出力結果の検証が完了するまでの非同期処理。</returns>
    [Fact(DisplayName = "render コマンドで重複範囲または入力と同じ出力パスを指定すると、書き込み前に拒否される")]
    public async Task Render_duplicate_ranges_and_same_input_output_are_rejected_before_write()
    {
        Directory.CreateDirectory(_directory);
        var output = Path.Combine(_directory, "preserved.pdf");
        File.WriteAllText(output, "sentinel");
        var result = await RunAsync("render", Input, "-o", output, "--format", "pdf", "--sheet", "折り返し",
            "--range", "A1:D8", "--range", "A1:D8");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal("sentinel", File.ReadAllText(output));
        var copy = Path.Combine(_directory, "same.xlsx");
        File.Copy(Input, copy);
        var bytes = File.ReadAllBytes(copy);
        result = await RunAsync("render", copy, "-o", copy, "--format", "pdf");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal(bytes, File.ReadAllBytes(copy));
    }

    /// <summary>dotnet でツールを実行し、終了コードと標準出力・標準エラーを非同期に取得します。</summary>
    /// <param name="arguments">ツールに渡すコマンドライン引数。</param>
    /// <returns>変換処理と出力結果の検証が完了するまでの非同期処理。</returns>
    private static async Task<Result> RunAsync(params string[] arguments)
    {
        var root = FindRepositoryRoot();
        var tool = ResolveToolAssemblyPath(root);
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(tool);
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new(process.ExitCode, await output, await error);
    }

    /// <summary>リポジトリ内の Debug、次いで Release の順に、ビルド済みツールの DLL を検索します。</summary>
    /// <param name="repositoryRoot">ソリューションファイルがあるリポジトリルート。</param>
    private static string ResolveToolAssemblyPath(string repositoryRoot)
    {
        var debug = Path.Combine(repositoryRoot, "src", "ExcelRenderer.Tool", "bin", "Debug", "net10.0", "ExcelRenderer.Tool.dll");
        if (File.Exists(debug))
        {
            return debug;
        }

        var release = Path.Combine(repositoryRoot, "src", "ExcelRenderer.Tool", "bin", "Release", "net10.0", "ExcelRenderer.Tool.dll");
        if (File.Exists(release))
        {
            return release;
        }

        throw new FileNotFoundException(
            $"Tool assembly not found. Checked both Debug and Release outputs:{Environment.NewLine}{debug}{Environment.NewLine}{release}");
    }

    /// <summary>実行ディレクトリから親をたどり、ExcelRenderer.slnx があるリポジトリルートを探します。</summary>
    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ExcelRenderer.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Repository root was not found.");
    }

    /// <summary>コマンドの終了コードがゼロであることを確認し、失敗時に標準出力と標準エラーを表示します。</summary>
    /// <param name="result">終了コードと出力内容を確認するコマンド実行結果。</param>
    private static void AssertSuccess(Result result) => Assert.True(result.ExitCode == 0, result.Output + result.Error);

    /// <summary>コマンドの終了コード、標準出力および標準エラーを保持します。</summary>
    private sealed record Result(int ExitCode, string Output, string Error);
}
