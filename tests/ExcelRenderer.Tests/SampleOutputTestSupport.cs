using ExcelRenderer.Drawing;
using ExcelRenderer.Excel;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using ExcelRenderer.PdfSharp;
using PdfSharp.Fonts;
using Xunit;

namespace ExcelRenderer.Tests;

/// <summary>
/// サンプルブックを使う出力テストに共通するパス解決、レイアウト生成およびフォント設定を提供します。
/// </summary>
internal static class SampleOutputTestSupport
{
    /// <summary>
    /// サンプル入力ディレクトリを基準にファイルパスを組み立てます。
    /// </summary>
    internal static readonly string InputDirectory = Path.Combine(AppContext.BaseDirectory, "SampleInputs");
    private static readonly string OutputDirectory = Path.Combine(AppContext.BaseDirectory, "SampleOutputs");

    /// <summary>
    /// Gets the rendering sample data. PNG および PDF の出力検証に使用するサンプル Excel ファイル名を列挙します。
    /// </summary>
    public static TheoryData<string> RenderSamples => new()
    {
        "01-japanese.xlsx",
        "02-image.xlsx",
        "03-wrapped-text.xlsx",
        "04-text-decoration.xlsx",
        "05-borders.xlsx",
        "06-layout-and-pagination.xlsx",
        "08-print-scaling.xlsx",
        "09-cell-border.xlsx",
    };

    /// <summary>
    /// サンプル Excel ブックの先頭シートを読み取り、描画レイアウトまで生成します。
    /// </summary>
    /// <param name="excelFileName">入力に使用するサンプル Excel ファイルの名前。</param>
    /// <returns>読み取った先頭シート、ページレイアウト、およびその描画命令をまとめた検証用データ。</returns>
    internal static SampleOutput ReadAndLayout(string excelFileName)
    {
        var excelPath = Path.Combine(InputDirectory, excelFileName);
        Assert.True(File.Exists(excelPath), $"入力 Excel ファイルが見つかりません: {excelPath}");
        var sheet = Assert.Single(new ExcelReader().Read(excelPath).Sheets);
        var layout = new ReportLayoutEngine(new PdfSharpTextMeasurer()).Layout(sheet);
        var commands = new DrawCommandGeneratorPass().Generate(layout);
        return new(sheet, layout, commands);
    }

    /// <summary>
    /// 入力ファイル名と接尾辞からスナップショット出力先のパスを生成します。
    /// </summary>
    /// <param name="excelFileName">入力に使用するサンプル Excel ファイルの名前。</param>
    /// <param name="suffix">入力ファイルのベース名に付加する出力形式別の接尾辞。</param>
    /// <returns>テスト出力ディレクトリ内に配置するファイルまたはディレクトリの絶対パス。</returns>
    internal static string OutputPath(string excelFileName, string suffix)
    {
        Directory.CreateDirectory(OutputDirectory);
        return Path.Combine(OutputDirectory, Path.GetFileNameWithoutExtension(excelFileName) + suffix);
    }

    /// <summary>
    /// テスト用の日本語フォントを既定フォントとして登録します。
    /// </summary>
    internal static void ConfigureJapaneseFont()
    {
        var fontPath = Path.Combine(AppContext.BaseDirectory, "NotoSansJP-VariableFont_wght.ttf");
        Assert.True(File.Exists(fontPath), $"日本語フォントが見つかりません: {fontPath}");
        GlobalFontSettings.FontResolver ??= new PdfSharpFontResolver("Noto Sans JP", fontPath, "游ゴシック", "Yu Gothic");
    }

    /// <summary>
    /// サンプルブックから読み取ったシートと、そのレイアウト結果および出力先を保持します。
    /// </summary>
    internal sealed record SampleOutput(
        ReportSheet Sheet,
        RenderDocument Layout,
        IReadOnlyList<DrawCommand> Commands);
}
