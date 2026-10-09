using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using ClosedXML.Excel;
using ExcelRenderer.Mapping;
using Xunit;

namespace ExcelRenderer.Tool.Tests;

public sealed partial class ToolIntegrationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public async Task Invoice_json_mapping_preserves_two_row_details_and_renders_xlsx_png_svg(int itemCount)
    {
        Directory.CreateDirectory(_directory);
        var sample = Path.Combine(FindRepositoryRoot(), "samples", "mapping", "invoice");
        var template = Path.Combine(sample, "template.xlsx");
        var originalTemplate = await File.ReadAllBytesAsync(template);
        var data = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(sample, "data.json")))!;
        var items = data["items"]!.AsArray();
        while (items.Count > itemCount)
        {
            items.RemoveAt(items.Count - 1);
        }

        var subtotal = items.Sum(item => item!["quantity"]!.GetValue<decimal>() * item["price"]!.GetValue<decimal>());
        var tax = items.Sum(item => item!["quantity"]!.GetValue<decimal>() * item["price"]!.GetValue<decimal>() * item["taxRate"]!.GetValue<decimal>());
        data["totals"] = new JsonObject { ["subtotal"] = subtotal, ["tax"] = tax, ["total"] = subtotal + tax };
        var json = Path.Combine(_directory, "data.json");
        await File.WriteAllTextAsync(json, data.ToJsonString());
        var originalJson = await File.ReadAllBytesAsync(json);
        var mapped = Path.Combine(_directory, "invoice.xlsx");
        AssertSuccess(await RunAsync("xlsx", template, "--data", json, "-o", mapped));
        using (var workbook = new XLWorkbook(mapped))
        {
            var sheet = workbook.Worksheet("請求書");
            Assert.Equal("株式会社サンプル 御中", sheet.Cell("B2").GetString());
            Assert.Equal("東京都千代田区丸の内1-2-3", sheet.Cell("B3").GetString());
            Assert.Equal("山田 太郎 様", sheet.Cell("B4").GetString());
            Assert.Equal("JPY", sheet.Cell("F4").GetString());
            Assert.Equal("2026/10/09", sheet.Cell("F3").GetString());
            Assert.Equal(XLDataType.Text, sheet.Cell("B5").DataType);
            Assert.Equal("2026-10-09", sheet.Cell("B5").GetString());
            Assert.Equal(XLDataType.Boolean, sheet.Cell("F5").DataType);
            Assert.False(sheet.Cell("F5").GetBoolean());
            Assert.Equal("27:30", sheet.Cell("B6").GetString());
            for (var index = 0; index < itemCount; index++)
            {
                var row = 9 + (2 * index);
                var item = items[index]!;
                Assert.Equal(XLDataType.Text, sheet.Cell(row, 1).DataType);
                Assert.Equal(item["code"]!.GetValue<int>().ToString("0000", CultureInfo.InvariantCulture), sheet.Cell(row, 1).GetString());
                Assert.Equal(item["name"]!.GetValue<string>(), sheet.Cell(row, 2).GetString());
                Assert.Equal(XLDataType.Number, sheet.Cell(row, 4).DataType);
                Assert.Equal((double)item["quantity"]!.GetValue<decimal>(), sheet.Cell(row, 4).GetDouble());
                Assert.Equal((double)item["price"]!.GetValue<decimal>(), sheet.Cell(row, 5).GetDouble());
                Assert.Equal($"D{row}*E{row}", sheet.Cell(row, 6).FormulaA1);
                Assert.Equal((double)(item["quantity"]!.GetValue<decimal>() * item["price"]!.GetValue<decimal>()), sheet.Cell(row, 6).CachedValue.GetNumber());
                Assert.Equal("#,##0.00;[Red](#,##0.00);\"-\"", sheet.Cell(row, 4).Style.NumberFormat.Format);
                Assert.Equal("\"¥\"#,##0.00;[Red]-\"¥\"#,##0.00;\"¥\"0.00", sheet.Cell(row, 6).Style.NumberFormat.Format);
                Assert.Equal("0.0%", sheet.Cell(row + 1, 6).Style.NumberFormat.Format);
                Assert.Equal((double)item["taxRate"]!.GetValue<decimal>(), sheet.Cell(row + 1, 6).GetDouble());
                Assert.Equal(item["note"]?.GetValue<string>() ?? string.Empty, sheet.Cell(row + 1, 2).GetString());
                Assert.Equal(item["note"] is null, sheet.Cell(row + 1, 2).IsEmpty());
                Assert.Equal(28, sheet.Row(row).Height);
                Assert.Equal(24, sheet.Row(row + 1).Height);
                Assert.Contains(sheet.MergedRanges, range => range.RangeAddress.ToString() == $"B{row}:C{row}");
                Assert.Contains(sheet.MergedRanges, range => range.RangeAddress.ToString() == $"B{row + 1}:D{row + 1}");
                Assert.Equal(XLBorderStyleValues.Thin, sheet.Cell(row, 6).Style.Border.BottomBorder);
                Assert.Equal("FFF1F5F9", sheet.Cell(row + 1, 1).Style.Fill.BackgroundColor.Color.ToArgb().ToString("X8", CultureInfo.InvariantCulture));
            }

            var footer = 9 + (2 * itemCount);
            Assert.Equal("小計", sheet.Cell(footer, 5).GetString());
            Assert.Equal((double)subtotal, sheet.Cell(footer, 6).GetDouble());
            Assert.Equal((double)tax, sheet.Cell(footer + 1, 6).GetDouble());
            Assert.Equal(XLDataType.Number, sheet.Cell(footer + 2, 6).DataType);
            Assert.Equal((double)(subtotal + tax), sheet.Cell(footer + 2, 6).GetDouble());
            Assert.True(sheet.Cell(footer + 2, 6).Style.Font.Bold);
            Assert.Equal((subtotal + tax).ToString("N2", CultureInfo.GetCultureInfo("de-DE")), sheet.Cell(footer + 5, 2).GetString());
            Assert.Equal("30 Nov 2026", sheet.Cell(footer + 5, 6).GetString());
            Assert.Equal(0, sheet.Cell(footer + 6, 2).GetDouble());
            Assert.Equal(1234567, sheet.Cell(footer + 6, 6).GetDouble());
            Assert.Equal(11, sheet.Cell(footer + 6, 6).Style.NumberFormat.NumberFormatId);
            Assert.Equal("**literal", sheet.Cell(footer + 7, 1).GetString());
            Assert.Equal(-1234.5, sheet.Cell(footer + 7, 2).GetDouble());
            Assert.True(sheet.Cell(footer + 7, 6).IsEmpty());
            Assert.Equal((subtotal + tax).ToString("C0", CultureInfo.GetCultureInfo("ja-JP")), sheet.Cell(footer + 8, 2).GetString());
            Assert.Equal(XLDataType.Text, sheet.Cell(footer + 8, 6).DataType);
            Assert.Equal("00123", sheet.Cell(footer + 8, 6).GetString());
            Assert.Equal(footer + 8, sheet.LastRowUsed()!.RowNumber());
            var printArea = Assert.Single(sheet.PageSetup.PrintAreas).RangeAddress;
            Assert.Equal(1, printArea.FirstAddress.RowNumber);
            Assert.Equal(1, printArea.FirstAddress.ColumnNumber);
            Assert.Equal(footer + 8, printArea.LastAddress.RowNumber);
            Assert.Equal(6, printArea.LastAddress.ColumnNumber);
            Assert.DoesNotContain(sheet.CellsUsed(), cell => cell.GetString().StartsWith("**@", StringComparison.Ordinal));
        }

        await AssertInvoiceRenderingsAsync(template, json, mapped);
        Assert.Equal(originalTemplate, await File.ReadAllBytesAsync(template));
        Assert.Equal(originalJson, await File.ReadAllBytesAsync(json));
    }

    [Fact]
    public async Task Invoice_clr_mapping_keeps_native_dates_times_and_decimals_for_rendering()
    {
        Directory.CreateDirectory(_directory);
        var sample = Path.Combine(FindRepositoryRoot(), "samples", "mapping", "invoice");
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(sample, "data.json")));
        var data = json.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => (object?)property.Value.Clone());
        data["invoice"] = new
        {
            number = "INV-2026-0042",
            issuedAt = new DateTime(2026, 10, 9),
            dueAt = new DateTimeOffset(2026, 11, 30, 12, 0, 0, TimeSpan.FromHours(9)),
            paid = true,
            duration = TimeSpan.FromHours(27),
        };
        data["totals"] = new { subtotal = 32364m, tax = 3246.4m, total = 35610.4m };
        var mapped = Path.Combine(_directory, "clr-invoice.xlsx");
        ExcelTemplateMapper.Map(Path.Combine(sample, "template.xlsx"), mapped, data);
        using (var workbook = new XLWorkbook(mapped))
        {
            var sheet = workbook.Worksheet(1);
            Assert.Equal(XLDataType.DateTime, sheet.Cell("B5").DataType);
            Assert.Equal(new DateTime(2026, 10, 9), sheet.Cell("B5").GetDateTime());
            Assert.Equal(XLDataType.TimeSpan, sheet.Cell("B6").DataType);
            Assert.Equal(TimeSpan.FromHours(27), sheet.Cell("B6").GetTimeSpan());
            Assert.True(sheet.Cell("F5").GetBoolean());
            Assert.Equal(35610.4, sheet.Cell("F17").GetDouble());
            Assert.Equal("30 Nov 2026", sheet.Cell("F20").GetString());
        }

        var markdown = Path.Combine(_directory, "clr.md");
        AssertSuccess(await RunAsync("markdown", mapped, "-o", markdown));
        var displayed = await File.ReadAllTextAsync(markdown);
        Assert.Contains("2026/10/09", displayed);
        Assert.Contains("27:00:00", displayed);
        Assert.Contains("TRUE", displayed);
        foreach (var format in new[] { "png", "svg" })
        {
            var output = Path.Combine(_directory, "clr-" + format);
            AssertSuccess(await RunAsync("render", mapped, "--format", format, "-o", output));
            Assert.Single(Directory.GetFiles(output, "*." + format));
        }
    }

    private async Task AssertInvoiceRenderingsAsync(string template, string json, string mapped)
    {
        foreach (var format in new[] { "png", "svg" })
        {
            var direct = Path.Combine(_directory, "direct-" + format);
            var staged = Path.Combine(_directory, "staged-" + format);
            var command = format == "png" ? "image" : "svg";
            string[] dpi = format == "png" ? ["--dpi", "96"] : [];
            AssertSuccess(await RunAsync([command, template, "--data", json, "-o", direct, .. dpi]));
            AssertSuccess(await RunAsync("render", mapped, "--format", format, "-o", staged));
            var actual = await File.ReadAllBytesAsync(Assert.Single(Directory.GetFiles(direct, "*." + format)));
            Assert.Equal(actual, await File.ReadAllBytesAsync(Assert.Single(Directory.GetFiles(staged, "*." + format))));
            if (format == "png")
            {
                Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, actual[..8]);
                Assert.InRange(BinaryPrimitives.ReadInt32BigEndian(actual.AsSpan(16, 4)), 500, 2000);
                Assert.InRange(BinaryPrimitives.ReadInt32BigEndian(actual.AsSpan(20, 4)), 500, 2500);
                Assert.True(actual.Length > 10000);
            }
            else
            {
                var document = XDocument.Parse(System.Text.Encoding.UTF8.GetString(actual));
                XNamespace ns = "http://www.w3.org/2000/svg";
                Assert.Equal(ns + "svg", document.Root!.Name);
                Assert.NotEmpty(document.Descendants(ns + "path"));
                Assert.Contains(document.Descendants(ns + "rect"), element => (string?)element.Attribute("fill") == "#243447");
                Assert.NotNull(document.Root.Attribute("viewBox"));
            }
        }

        var markdown = Path.Combine(_directory, "display.md");
        AssertSuccess(await RunAsync("markdown", template, "--data", json, "-o", markdown));
        var displayed = await File.ReadAllTextAsync(markdown);
        Assert.Contains("株式会社サンプル 御中", displayed);
        Assert.Contains("2026/10/09", displayed);
        Assert.Contains("FALSE", displayed);
        Assert.Contains("30 Nov 2026", displayed);
        Assert.Contains("1.23E+06", displayed);
        Assert.Contains("(1,234.50)", displayed);
        Assert.Contains("00123", displayed);
        Assert.DoesNotContain("**@", displayed);
    }
}
