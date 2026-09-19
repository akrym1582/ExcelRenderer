using System.Net;
using System.Text.RegularExpressions;
using ExcelRenderer.Excel;
using ExcelRenderer.Markdown;
using Xunit;

namespace ExcelRenderer.Tests;

public sealed class MarkdownSampleOutputTests
{
    public static IEnumerable<object[]> Samples => Directory
        .EnumerateFiles(SampleOutputTestSupport.InputDirectory, "*.xlsx")
        .OrderBy(path => path)
        .Select(path => new object[] { Path.GetFileName(path) });

    [Theory]
    [MemberData(nameof(Samples))]
    public async Task Generates_markdown_from_prebuilt_excel(string excelFileName)
    {
        var inputPath = Path.Combine(SampleOutputTestSupport.InputDirectory, excelFileName);
        // Isolate image directories because different workbooks can share sheet names.
        var outputDirectory = SampleOutputTestSupport.OutputPath(excelFileName, "-markdown");
        if (Directory.Exists(outputDirectory)) Directory.Delete(outputDirectory, true);

        await ExcelMarkdownConverter.ConvertAsync(inputPath, outputDirectory);

        var markdownPath = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(excelFileName) + ".md");
        var markdown = await File.ReadAllTextAsync(markdownPath);
        var document = new ExcelReader().Read(inputPath);
        Assert.StartsWith("# " + excelFileName, markdown);
        Assert.Equal(document.Sheets.Count, Regex.Matches(markdown, @"(?m)^## Sheet: ").Count);
        foreach (var sheet in document.Sheets)
        {
            Assert.Contains("## Sheet: " + sheet.Name, markdown);
            var firstText = sheet.Cells.OrderBy(cell => cell.Key.Row).ThenBy(cell => cell.Key.Column)
                .First(cell => !string.IsNullOrWhiteSpace(cell.Value.Text)).Value.Text!;
            var decoded = WebUtility.HtmlDecode(markdown).Replace("<br>", "\n");
            Assert.Contains(firstText.Replace("\r", ""), decoded);
        }

        var images = document.Sheets.SelectMany(sheet => sheet.Images ?? []).ToArray();
        var links = Regex.Matches(markdown, @"!\[Image at [^\]]+\]\(([^)]+)\)");
        Assert.Equal(images.Length, links.Count);
        for (var i = 0; i < links.Count; i++)
        {
            var imagePath = Path.Combine(outputDirectory, Uri.UnescapeDataString(links[i].Groups[1].Value));
            Assert.True(File.Exists(imagePath), $"画像が出力されていません: {imagePath}");
            Assert.Equal(images[i].ImageBytes, await File.ReadAllBytesAsync(imagePath));
        }

        if (excelFileName == "07-multiple-sheets.xlsx")
        {
            Assert.Contains("売上シート", markdown);
            Assert.Contains("在庫シート", markdown);
        }
        if (excelFileName == "06-layout-and-pagination.xlsx")
        {
            Assert.Contains("結合セル（A1:C1）", markdown);
            Assert.Contains("colspan=\"3\"", markdown);
            Assert.DoesNotContain("R2C2", markdown); // Hidden column B.
            Assert.DoesNotContain("R4C1", markdown); // Hidden row 4.
        }
    }
}
