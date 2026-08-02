using PdfSharp.Fonts;
using ExcelRenderer.Drawing;
using ExcelRenderer.Excel;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using ExcelRenderer.PdfSharp;
using Xunit;

namespace ExcelRenderer.Tests;

internal static class SampleOutputTestSupport
{
    internal static readonly string InputDirectory = Path.Combine(AppContext.BaseDirectory, "SampleInputs");
    private static readonly string OutputDirectory = Path.Combine(AppContext.BaseDirectory, "SampleOutputs");

    public static TheoryData<string> RenderSamples => new()
    {
        "01-japanese.xlsx",
        "02-image.xlsx",
        "03-wrapped-text.xlsx",
        "04-text-decoration.xlsx",
        "05-borders.xlsx",
        "06-layout-and-pagination.xlsx"
    };

    internal static SampleOutput ReadAndLayout(string excelFileName)
    {
        var excelPath = Path.Combine(InputDirectory, excelFileName);
        Assert.True(File.Exists(excelPath), $"入力 Excel ファイルが見つかりません: {excelPath}");
        var sheet = Assert.Single(new ExcelReader().Read(excelPath).Sheets);
        var layout = new ReportLayoutEngine(new PdfSharpTextMeasurer()).Layout(sheet);
        var commands = new DrawCommandGeneratorPass().Generate(layout);
        return new(sheet, layout, commands);
    }

    internal static string OutputPath(string excelFileName, string suffix)
    {
        Directory.CreateDirectory(OutputDirectory);
        return Path.Combine(OutputDirectory, Path.GetFileNameWithoutExtension(excelFileName) + suffix);
    }

    internal static void ConfigureJapaneseFont()
    {
        var fontPath = Path.Combine(AppContext.BaseDirectory, "NotoSansJP-VariableFont_wght.ttf");
        Assert.True(File.Exists(fontPath), $"日本語フォントが見つかりません: {fontPath}");
        GlobalFontSettings.FontResolver ??= new PdfSharpFontResolver("Noto Sans JP", fontPath);
    }

    internal sealed record SampleOutput(
        ReportSheet Sheet,
        RenderDocument Layout,
        IReadOnlyList<DrawCommand> Commands);
}
