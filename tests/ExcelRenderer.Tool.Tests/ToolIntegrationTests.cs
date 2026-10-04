using System.Diagnostics;
using System.Text;
using Xunit;

namespace ExcelRenderer.Tool.Tests;

/// <summary>
/// コマンドラインツールの各変換コマンド、入力検証およびエラー表示を検証します。
/// </summary>
public sealed class ToolIntegrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ExcelRenderer.Tool.Tests", Guid.NewGuid().ToString("N"));

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
    [Fact]
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
    [Fact]
    public async Task Image_command_creates_png_files()
    {
        var output = Path.Combine(_directory, "images");
        var result = await RunAsync("image", Input, "-o", output, "--dpi", "72");
        AssertSuccess(result);
        var png = Assert.Single(Directory.GetFiles(output, "*.png"));
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4e, 0x47 }, File.ReadAllBytes(png)[..4]);
    }

    /// <summary>svg コマンドが Excel 入力からページ単位の SVG ファイルを生成することを検証します。</summary>
    /// <returns>非同期の検証処理を表すタスク。</returns>
    [Fact]
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
    [Fact]
    public async Task Svg_command_rejects_dpi()
    {
        var result = await RunAsync("svg", Input, "-o", Path.Combine(_directory, "svg"), "--dpi", "72");
        Assert.NotEqual(0, result.ExitCode);
    }

    /// <summary>
    /// md エイリアスが Excel 入力から Markdown ファイルを生成することを検証します。
    /// </summary>
    /// <returns>非同期の検証処理を表すタスク。</returns>
    [Fact]
    public async Task Md_alias_creates_markdown()
    {
        var output = Path.Combine(_directory, "nested", "report.md");
        var result = await RunAsync("md", Input, "-o", output);
        AssertSuccess(result);
        Assert.Contains("折り返し", await File.ReadAllTextAsync(output));
    }

    /// <summary>render コマンドが RenderAsync 経由で PDF とマニフェストを生成することを検証します。</summary>
    /// <returns>非同期の検証処理を表すタスク。</returns>
    [Fact]
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
    [Fact]
    public async Task Render_command_creates_svg_in_output_directory()
    {
        var output = Path.Combine(_directory, "rendered-svg");
        var result = await RunAsync("render", Input, "-o", output, "--format", "svg", "--sheet", "折り返し");
        AssertSuccess(result);
        Assert.Single(Directory.GetFiles(output, "*.svg"));
    }

    /// <summary>render コマンドが TTE 拡張子の有効な外部フォントを受け付けます。</summary>
    [Fact]
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
    [Fact]
    public async Task Render_command_rejects_missing_explicit_font_file()
    {
        var result = await RunAsync("render", Input, "-o", Path.Combine(_directory, "font-svg"), "--format", "svg",
            "--font-file", Path.Combine(_directory, "missing.tte"));

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Font file was not found", result.Error);
    }

    /// <summary>すべての変換コマンドが共通フォントオプションをヘルプに公開することを検証します。</summary>
    [Theory]
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
    [Fact]
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
    [Theory]
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
    [Fact]
    public async Task Render_command_creates_continuous_svg()
    {
        var output = Path.Combine(_directory, "continuous-svg");
        var result = await RunAsync("render", Input, "-o", output, "--format", "svg", "--sheet", "折り返し", "--image-layout", "continuous");

        AssertSuccess(result);
        Assert.Single(Directory.GetFiles(output, "*.svg"));
    }

    /// <summary>連続レイアウトでページ選択を指定すると説明的に失敗することを検証します。</summary>
    [Fact]
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
    [Theory]
    [InlineData("md")]
    [InlineData("md", "not-found.xlsx", "-o", "output.md")]
    public async Task Invalid_input_fails(params string[] arguments) => Assert.NotEqual(0, (await RunAsync(arguments)).ExitCode);

    /// <summary>
    /// 範囲外の DPI がコマンドライン解析時に拒否されることを検証します。
    /// </summary>
    /// <returns>非同期の検証処理を表すタスク。</returns>
    [Fact]
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
    [Fact]
    public async Task Unknown_sheet_fails_without_a_stack_trace()
    {
        var result = await RunAsync("image", Input, "-o", Path.Combine(_directory, "images"), "--sheet", "UnknownSheet");
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Worksheet was not found", result.Error);
        Assert.DoesNotContain(" at ", result.Error);
    }

    [Fact]
    public async Task Render_range_trim_and_hyperlink_options_create_a_pdf_after_preflight()
    {
        var output = Path.Combine(_directory, "range.pdf");
        var result = await RunAsync("render", Input, "-o", output, "--format", "pdf", "--sheet", "折り返し",
            "--range", "A1:D8", "--trim", "--trim-padding", "2", "--hyperlinks", "none");
        AssertSuccess(result);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(File.ReadAllBytes(output), 0, 4));
    }

    [Theory]
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

    [Fact]
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

    private static void AssertSuccess(Result result) => Assert.True(result.ExitCode == 0, result.Output + result.Error);

    private sealed record Result(int ExitCode, string Output, string Error);
}
