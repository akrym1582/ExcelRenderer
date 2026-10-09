using System.Text.Json.Nodes;
using ClosedXML.Excel;
using Xunit;

namespace ExcelRenderer.Tool.Tests;

public sealed partial class ToolIntegrationTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task Invoice_page_breaks_follow_nested_two_row_details_without_splitting_them(int pageCount)
    {
        Directory.CreateDirectory(_directory);
        var sample = Path.Combine(FindRepositoryRoot(), "samples", "mapping", "invoice", "page-breaks");
        var template = Path.Combine(sample, "template.xlsx");
        var originalTemplate = await File.ReadAllBytesAsync(template);
        var data = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(sample, "data.json")))!;
        var pages = data["pages"]!.AsArray();
        while (pages.Count > pageCount)
        {
            pages.RemoveAt(pages.Count - 1);
        }

        var json = Path.Combine(_directory, "pages.json");
        await File.WriteAllTextAsync(json, data.ToJsonString());
        var mapped = Path.Combine(_directory, "pages.xlsx");
        AssertSuccess(await RunAsync("xlsx", template, "--data", json, "-o", mapped));
        using (var workbook = new XLWorkbook(mapped))
        {
            var sheet = workbook.Worksheet("請求書");
            var nextRow = 1;
            var expectedBreaks = new List<int>();
            foreach (var page in pages)
            {
                if (nextRow > 1)
                {
                    expectedBreaks.Add(nextRow - 1);
                }

                Assert.True(sheet.Cell(nextRow, 1).IsEmpty());
                Assert.Equal(page!["title"]!.GetValue<string>(), sheet.Cell(nextRow, 2).GetString());
                var items = page["items"]!.AsArray();
                for (var index = 0; index < items.Count; index++)
                {
                    var row = nextRow + 8 + (2 * index);
                    var item = items[index]!;
                    Assert.Equal(item["name"]!.GetValue<string>(), sheet.Cell(row, 2).GetString());
                    Assert.Equal(item["note"]?.GetValue<string>() ?? string.Empty, sheet.Cell(row + 1, 2).GetString());
                    Assert.Equal((double)item["taxRate"]!.GetValue<decimal>(), sheet.Cell(row + 1, 6).GetDouble());
                    Assert.Equal($"D{row}*E{row}", sheet.Cell(row, 6).FormulaA1);
                    Assert.Equal((double)(item["quantity"]!.GetValue<decimal>() * item["price"]!.GetValue<decimal>()), sheet.Cell(row, 6).CachedValue.GetNumber());
                    Assert.Contains(sheet.MergedRanges, range => range.RangeAddress.ToString() == $"B{row + 1}:D{row + 1}");
                }

                var footer = nextRow + 8 + (2 * items.Count);
                Assert.Equal("小計", sheet.Cell(footer, 5).GetString());
                Assert.Equal((double)page["totals"]!["subtotal"]!.GetValue<decimal>(), sheet.Cell(footer, 6).GetDouble());
                Assert.Equal((double)page["totals"]!["total"]!.GetValue<decimal>(), sheet.Cell(footer + 2, 6).GetDouble());
                nextRow += 17 + (2 * items.Count);
            }

            Assert.Equal(expectedBreaks, sheet.PageSetup.RowBreaks.Order().ToList());
            Assert.Empty(sheet.PageSetup.ColumnBreaks);
            Assert.Equal(100, sheet.PageSetup.Scale);
            Assert.Equal(0, sheet.PageSetup.PagesWide);
            Assert.Equal(0, sheet.PageSetup.PagesTall);
            Assert.Equal(nextRow - 1, sheet.LastRowUsed()!.RowNumber());
            Assert.Equal(nextRow, Assert.Single(sheet.PageSetup.PrintAreas).RangeAddress.LastAddress.RowNumber);
            Assert.DoesNotContain(sheet.CellsUsed(), cell => cell.GetString().StartsWith("**@", StringComparison.Ordinal));
        }

        foreach (var format in new[] { "png", "svg" })
        {
            var direct = Path.Combine(_directory, "direct-" + format);
            var staged = Path.Combine(_directory, "staged-" + format);
            AssertSuccess(await RunAsync("render", template, "--data", json, "--format", format, "-o", direct));
            AssertSuccess(await RunAsync("render", mapped, "--format", format, "-o", staged));
            var actualPages = Directory.GetFiles(direct, "*." + format).Order().ToArray();
            var stagedPages = Directory.GetFiles(staged, "*." + format).Order().ToArray();
            Assert.Equal(pageCount, actualPages.Length);
            Assert.Equal(pageCount, stagedPages.Length);
            for (var index = 0; index < pageCount; index++)
            {
                var actual = await File.ReadAllBytesAsync(actualPages[index]);
                Assert.Equal(actual, await File.ReadAllBytesAsync(stagedPages[index]));

                // An isolated page is a content oracle: both detail rows and its footer
                // must appear on this page, with no adjacent-page content or extra leading page.
                var isolatedData = data.DeepClone();
                isolatedData["pages"] = new JsonArray(pages[index]!.DeepClone());
                var isolatedJson = Path.Combine(_directory, $"page-{index}.json");
                await File.WriteAllTextAsync(isolatedJson, isolatedData.ToJsonString());
                var isolatedOutput = Path.Combine(_directory, $"isolated-{index}-{format}");
                AssertSuccess(await RunAsync("render", template, "--data", isolatedJson, "--format", format, "-o", isolatedOutput));
                var expected = Assert.Single(Directory.GetFiles(isolatedOutput, "*." + format));
                Assert.Equal(await File.ReadAllBytesAsync(expected), actual);
            }
        }

        Assert.Equal(originalTemplate, await File.ReadAllBytesAsync(template));
    }
}
