using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using ExcelRenderer.Mapping;
using ExcelRenderer.OpenXml.Reader.Model.Cells;
using static ExcelRenderer.TestFixtures.WorkbookAssertions;
using ExcelRenderer.TestFixtures;
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
        using (var workbook = WorkbookProbe.Open(mapped))
        {
            var sheet = workbook;
            Assert.Equal("株式会社サンプル 御中", Text(sheet, "B2"));
            Assert.Equal("東京都千代田区丸の内1-2-3", Text(sheet, "B3"));
            Assert.Equal("山田 太郎 様", Text(sheet, "B4"));
            Assert.Equal("JPY", Text(sheet, "F4"));
            Assert.Equal("2026/10/09", Text(sheet, "F3"));
            Assert.Equal(CellValueType.String, Cell(sheet, "B5").Value.Type);
            Assert.Equal("2026-10-09", Text(sheet, "B5"));
            Assert.Equal(CellValueType.Boolean, Cell(sheet, "F5").Value.Type);
            Assert.False(Cell(sheet, "F5").Value.Boolean == true);
            Assert.Equal("27:30", Text(sheet, "B6"));
            for (var index = 0; index < itemCount; index++)
            {
                var row = 9 + (2 * index);
                var item = items[index]!;
                Assert.Equal(CellValueType.String, Cell(sheet, row, 1).Value.Type);
                Assert.Equal(item["code"]!.GetValue<int>().ToString("0000", CultureInfo.InvariantCulture), Text(sheet, row, 1));
                Assert.Equal(item["name"]!.GetValue<string>(), Text(sheet, row, 2));
                Assert.Equal(CellValueType.Number, Cell(sheet, row, 4).Value.Type);
                Assert.Equal((double)item["quantity"]!.GetValue<decimal>(), Number(sheet, row, 4));
                Assert.Equal((double)item["price"]!.GetValue<decimal>(), Number(sheet, row, 5));
                Assert.False(Cell(sheet, row, 6).Formula != null);
                Assert.Equal((double)(item["quantity"]!.GetValue<decimal>() * item["price"]!.GetValue<decimal>()), Number(sheet, row, 6));
                Assert.Equal("#,##0.00;[Red](#,##0.00);\"-\"", Style(sheet, row, 4).NumberFormatCode);
                Assert.Equal("\"¥\"#,##0.00;[Red]-\"¥\"#,##0.00;\"¥\"0.00", Style(sheet, row, 6).NumberFormatCode);
                Assert.Equal("0.0%", Style(sheet, row + 1, 6).NumberFormatCode);
                Assert.Equal((double)item["taxRate"]!.GetValue<decimal>(), Number(sheet, row + 1, 6));
                Assert.Equal(item["note"]?.GetValue<string>() ?? string.Empty, Text(sheet, row + 1, 2));
                Assert.Equal(item["note"] is null, Cell(sheet, row + 1, 2).Value.Type == CellValueType.Blank);
                Assert.Equal(28, Row(sheet, row).HeightPt);
                Assert.Equal(24, Row(sheet, row + 1).HeightPt);
                Assert.Contains(Info(sheet).MergedRanges, range => range.ToString() == $"B{row}:C{row}");
                Assert.Contains(Info(sheet).MergedRanges, range => range.ToString() == $"B{row + 1}:D{row + 1}");
                Assert.Equal("thin", Style(sheet, row, 6).Border.Children.Single(element => element.Name == "bottom").Attributes["style"]);
                Assert.Equal("FFF1F5F9", Fill(sheet, row + 1, 1));
            }

            var footer = 9 + (2 * itemCount);
            Assert.Equal("小計", Text(sheet, footer, 5));
            Assert.Equal((double)subtotal, Number(sheet, footer, 6));
            Assert.Equal((double)tax, Number(sheet, footer + 1, 6));
            Assert.Equal(CellValueType.Number, Cell(sheet, footer + 2, 6).Value.Type);
            Assert.Equal((double)(subtotal + tax), Number(sheet, footer + 2, 6));
            Assert.True(Style(sheet, footer + 2, 6).FontProperties.Bold == true);
            Assert.Equal((subtotal + tax).ToString("N2", CultureInfo.GetCultureInfo("de-DE")), Text(sheet, footer + 5, 2));
            Assert.Equal("30 Nov 2026", Text(sheet, footer + 5, 6));
            Assert.Equal(0, Number(sheet, footer + 6, 2));
            Assert.Equal(1234567, Number(sheet, footer + 6, 6));
            Assert.Equal(11, Style(sheet, footer + 6, 6).NumberFormatId);
            Assert.Equal("**literal", Text(sheet, footer + 7, 1));
            Assert.Equal(-1234.5, Number(sheet, footer + 7, 2));
            Assert.True(Cell(sheet, footer + 7, 6).Value.Type == CellValueType.Blank);
            Assert.Equal((subtotal + tax).ToString("C0", CultureInfo.GetCultureInfo("ja-JP")), Text(sheet, footer + 8, 2));
            Assert.Equal(CellValueType.String, Cell(sheet, footer + 8, 6).Value.Type);
            Assert.Equal("00123", Text(sheet, footer + 8, 6));
            Assert.Equal(footer + 8, LastContentRow(sheet));
            var printArea = Info(sheet).PrintReferences.Single(item => item.Name == "_xlnm.Print_Area").Ranges.Single();
            Assert.Equal(1, printArea.FirstRow);
            Assert.Equal(1, printArea.FirstColumn);
            Assert.Equal(footer + 8, printArea.LastRow);
            Assert.Equal(6, printArea.LastColumn);
            Assert.DoesNotContain(Cells(sheet), cell => cell.Value.Text?.StartsWith("**@", StringComparison.Ordinal) == true);
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
        using (var workbook = WorkbookProbe.Open(mapped))
        {
            var sheet = workbook;
            Assert.Equal(CellValueType.Number, Cell(sheet, "B5").Value.Type);
            Assert.Equal(new DateTime(2026, 10, 9), Date(sheet, "B5"));
            Assert.Equal(CellValueType.Number, Cell(sheet, "B6").Value.Type);
            Assert.Equal(TimeSpan.FromHours(27), Duration(sheet, "B6"));
            Assert.True(Cell(sheet, "F5").Value.Boolean == true);
            Assert.Equal(35610.4, Number(sheet, "F17"));
            Assert.Equal("30 Nov 2026", Text(sheet, "F20"));
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
