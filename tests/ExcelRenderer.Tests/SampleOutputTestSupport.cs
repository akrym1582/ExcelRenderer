using ExcelRenderer.Drawing;
using ExcelRenderer.Excel;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using ExcelRenderer.PdfSharp;
using PdfSharp.Fonts;
using Xunit;

namespace ExcelRenderer.Tests;

/// <summary>
/// SampleOutputTestSupport が表すデータと操作を提供します.
/// </summary>
internal static class SampleOutputTestSupport
{
    /// <summary>
    /// Combine を実行します.
    /// </summary>
    internal static readonly string InputDirectory = Path.Combine(AppContext.BaseDirectory, "SampleInputs");
    private static readonly string OutputDirectory = Path.Combine(AppContext.BaseDirectory, "SampleOutputs");

    /// <summary>
    /// Gets the value. 対応する値を取得または設定します.
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
    /// ReadAndLayout を実行します.
    /// </summary>
    /// <param name="excelFileName">excelFileName に渡す値です。</param>
    /// <returns>処理によって得られた結果を返します。</returns>
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
    /// OutputPath を実行します.
    /// </summary>
    /// <param name="excelFileName">excelFileName に渡す値です。</param>
    /// <param name="suffix">suffix に渡す値です。</param>
    /// <returns>処理によって得られた結果を返します。</returns>
    internal static string OutputPath(string excelFileName, string suffix)
    {
        Directory.CreateDirectory(OutputDirectory);
        return Path.Combine(OutputDirectory, Path.GetFileNameWithoutExtension(excelFileName) + suffix);
    }

    /// <summary>
    /// ConfigureJapaneseFont を実行します.
    /// </summary>
    internal static void ConfigureJapaneseFont()
    {
        var fontPath = Path.Combine(AppContext.BaseDirectory, "NotoSansJP-VariableFont_wght.ttf");
        Assert.True(File.Exists(fontPath), $"日本語フォントが見つかりません: {fontPath}");
        GlobalFontSettings.FontResolver ??= new PdfSharpFontResolver("Noto Sans JP", fontPath, "游ゴシック", "Yu Gothic");
    }

    /// <summary>
    /// SampleOutput が表すデータと操作を提供します.
    /// </summary>
    internal sealed record SampleOutput(
        ReportSheet Sheet,
        RenderDocument Layout,
        IReadOnlyList<DrawCommand> Commands);
}
