using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.TestFixtures;
using UglyToad.PdfPig;
using Xunit;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.Tests;

public sealed class MainCoreExpectedDifferenceTests
{
    [Theory]
    [InlineData("richtext")]
    [InlineData("rotation")]
    [InlineData("ivs")]
    [InlineData("link")]
    [InlineData("conditional-format")]
    public async Task Core_omits_only_the_documented_attribute_and_preserves_other_cells(string attribute)
    {
        var bytes = MainCoreParityTests.Fixture("dimensions-match");
        var (baselineMain, baselineCore, _, _) = await MainCoreParityTests.Render(bytes, ["data"]);
        using var stream = new MemoryStream();
        stream.Write(bytes);
        using (var workbook = SpreadsheetDocument.Open(stream, true))
        {
            var part = workbook.WorkbookPart!.WorksheetParts.First();
            var cell = part.Worksheet.Descendants<S.Cell>().First();
            if (attribute == "richtext")
            {
                cell.InlineString = new S.InlineString(new S.Run(new S.RunProperties(new S.FontSize { Val = 20 }, new S.Color { Rgb = "FFFF0000" }), new S.Text("共通")), new S.Run(new S.Text(" ABC") { Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve }));
            }
            if (attribute == "rotation")
            {
                var formats = workbook.WorkbookPart.WorkbookStylesPart!.Stylesheet.CellFormats!;
                cell.StyleIndex = (uint)formats.ChildElements.Count;
                formats.Append(new S.CellFormat(new S.Alignment { TextRotation = 30 }));
            }
            if (attribute == "ivs") cell.InlineString!.GetFirstChild<S.Text>()!.Text = "共\uFE00通 ABC";
            if (attribute == "link")
            {
                var relationship = part.AddHyperlinkRelationship(new Uri("https://example.com/"), true);
                part.Worksheet.Append(new S.Hyperlinks(new S.Hyperlink { Reference = "A1", Id = relationship.Id }));
            }
            if (attribute == "conditional-format")
            {
                workbook.WorkbookPart.WorkbookStylesPart!.Stylesheet.Append(new S.DifferentialFormats(new S.DifferentialFormat(new S.Font(new S.Color { Rgb = "FFFF0000" }))));
                part.Worksheet.Append(new S.ConditionalFormatting(new S.ConditionalFormattingRule(new S.Formula("TRUE")) { Type = S.ConditionalFormatValues.Expression, FormatId = 0, Priority = 1 }) { SequenceOfReferences = new DocumentFormat.OpenXml.ListValue<DocumentFormat.OpenXml.StringValue> { InnerText = "A1" } });
            }
        }
        var (main, core, _, _) = await MainCoreParityTests.Render(stream.ToArray(), ["data"]);
        await Retain("difference-" + attribute, stream.ToArray(), main, core);
        MainCoreParityTests.Compare(baselineCore, core);
        using var a = PdfDocument.Open(main);
        using var b = PdfDocument.Open(baselineMain);
        Assert.Equal(b.NumberOfPages, a.NumberOfPages);
        // The second row is outside the changed region, including for rich text and links.
        var unchanged = a.GetPage(1).Letters.Where(letter => letter.Value == "r").ToArray();
        Assert.Equal(b.GetPage(1).Letters.Where(letter => letter.Value == "r").Select(letter => letter.StartBaseLine), unchanged.Select(letter => letter.StartBaseLine));
        if (attribute == "link")
        {
            Assert.Single(a.GetPage(1).GetHyperlinks());
            using var c = PdfDocument.Open(core);
            Assert.Empty(c.GetPage(1).GetHyperlinks());
        }
        if (attribute == "richtext") Assert.Contains(a.GetPage(1).Letters, letter => letter.FontSize > 19);
        if (attribute == "rotation") Assert.NotEqual(b.GetPage(1).Letters[0].EndBaseLine, a.GetPage(1).Letters[0].EndBaseLine);
        if (attribute == "conditional-format") Assert.NotEqual(b.GetPage(1).Letters[0].Color.ToRGBValues(), a.GetPage(1).Letters[0].Color.ToRGBValues());
    }

    [Fact]
    public async Task Default_sheet_selection_includes_hidden_sheets_in_both_current_apis()
    {
        using var stream = new MemoryStream();
        stream.Write(MainCoreParityTests.Fixture("portrait"));
        using (var workbook = SpreadsheetDocument.Open(stream, true))
        {
            SdkWorkbook.AddSheet(workbook.WorkbookPart!, new S.Worksheet(new S.SheetData(new S.Row(SdkWorkbook.Text("A1", "HIDDEN SHEET")) { RowIndex = 1 })), "hidden", S.SheetStateValues.Hidden);
        }
        var (main, core, _, _) = await MainCoreParityTests.Render(stream.ToArray(), null);
        await Retain("default-selection", stream.ToArray(), main, core);
        using var a = PdfDocument.Open(main);
        using var b = PdfDocument.Open(core);
        Assert.Equal(2, a.NumberOfPages);
        Assert.Equal(2, b.NumberOfPages);
        Assert.Equal(a.GetPage(1).Text, b.GetPage(1).Text);
        Assert.Contains("HIDDEN SHEET", b.GetPage(2).Text);
    }

    private static async Task Retain(string id, byte[] input, byte[] main, byte[] core)
    {
        Directory.CreateDirectory(MainCoreParityTests.EvidenceDirectory);
        var prefix = Path.Combine(MainCoreParityTests.EvidenceDirectory, id);
        await File.WriteAllBytesAsync(prefix + ".xlsx", input);
        await File.WriteAllBytesAsync(prefix + "-main.pdf", main);
        await File.WriteAllBytesAsync(prefix + "-core.pdf", core);
        // TRX records assertion status; retain the exact inputs/outputs even when an assertion fails.
        await File.WriteAllTextAsync(prefix + ".json", System.Text.Json.JsonSerializer.Serialize(new
        {
            id,
            input_sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(input)),
            font_sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(MainCoreParityTests.FontPath))),
            assertion_result = "See test TRX",
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

}
