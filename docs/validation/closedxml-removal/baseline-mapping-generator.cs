using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using ClosedXML.Excel;
using ExcelRenderer.Mapping;
using ExcelRenderer.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using SkiaSharp;
using S = DocumentFormat.OpenXml.Spreadsheet;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var destination = Path.GetFullPath(args[0]);
Directory.CreateDirectory(destination);
var results = new List<object>();
foreach (var feature in new[] { "print-area", "static-name", "external-link", "internal-link", "picture", "comment", "table", "filter", "conditional", "validation" })
{
    using var workbook = new XLWorkbook();
    var sheet = workbook.AddWorksheet("Template");
    sheet.Cell("A1").Value = "Header";
    sheet.Cell("A2").Value = "**items[*].name";
    sheet.Cell("B2").Value = "**items[*].amount";
    sheet.Cell("A3").Value = "Footer";
    sheet.Cell("A6").Value = "Fixed";
    sheet.Cell("B6").Value = "Value";
    sheet.Cell("A7").Value = "Data";
    sheet.Cell("B7").Value = 7;
    sheet.PageSetup.PrintAreas.Add("A1:C8");
    switch (feature)
    {
        case "static-name": workbook.DefinedNames.Add("FooterValue", "'Template'!$A$3"); break;
        case "external-link": sheet.Cell("A2").SetHyperlink(new XLHyperlink("https://example.invalid/items")); break;
        case "internal-link": sheet.Cell("A2").SetHyperlink(new XLHyperlink("Template!A3")); break;
        case "picture":
            using (var bitmap = new SKBitmap(3, 3))
            {
                bitmap.Erase(SKColors.Red);
                using var image = SKImage.FromBitmap(bitmap);
                using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
                using var stream = new MemoryStream(encoded.ToArray());
                sheet.AddPicture(stream, "logo").MoveTo(sheet.Cell("C6"));
            }
            break;
        case "comment": sheet.Cell("A2").GetComment().AddText("Repeated comment"); break;
        case "table": sheet.Range("A6:B7").CreateTable("FixedTable"); break;
        case "filter": sheet.Range("A6:B7").SetAutoFilter(); break;
        case "conditional": sheet.Range("A2:B2").AddConditionalFormat().WhenIsBlank().Fill.SetBackgroundColor(XLColor.Red); break;
        case "validation": sheet.Cell("B2").CreateDataValidation().WholeNumber.Between(1, 20); break;
    }
    var path = Path.Combine(destination, feature + ".xlsx");
    workbook.SaveAs(path);
    using var capabilitySource = File.OpenRead(path);
    var reasons = WorkbookWriteCapabilities.Inspect(capabilitySource);
    foreach (var count in new[] { 0, 3 })
    {
        var data = new { items = Enumerable.Range(1, count).Select(index => new { name = "Item " + index, amount = index * 10 }).ToArray() };
        object? expected = null;
        string? legacyError = null;
        using (var input = File.OpenRead(path))
        using (var output = new MemoryStream())
        {
            try
            {
                ExcelTemplateMapper.Map(input, output, data);
                var expectedPath = Path.Combine(destination, feature + "-expected-" + count + ".xlsx");
                File.WriteAllBytes(expectedPath, output.ToArray());
                expected = Snapshot(expectedPath);
            }
            catch (Exception error) { legacyError = error.GetType().Name + ": " + error.Message; }
        }
        string? openXmlError = null;
        try
        {
            using var input = File.OpenRead(path);
            using var output = new MemoryStream();
            ExcelTemplateMapper.Map(input, output, data, new() { Backend = MappingBackend.OpenXml });
        }
        catch (Exception error) { openXmlError = error.GetType().Name + ": " + error.Message; }
        results.Add(new { Feature = feature, Count = count, InputSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), CapabilityReasons = reasons, LegacyError = legacyError, OpenXmlError = openXmlError, Expected = expected });
    }
}
File.WriteAllText(Path.Combine(destination, "baseline.json"), JsonSerializer.Serialize(new { BaselineCommit = "24c9dea4d67190951d152eef56f76abc943ef733", Culture = "InvariantCulture", Results = results }, new JsonSerializerOptions { WriteIndented = true }));
static object Snapshot(string path)
{
    using var package = SpreadsheetDocument.Open(path, false);
    var workbook = package.WorkbookPart!;
    var sheet = workbook.WorksheetParts.Single();
    var shared = workbook.SharedStringTablePart?.SharedStringTable?.Elements<S.SharedStringItem>().Select(item => item.InnerText).ToArray() ?? [];
    return new
    {
        Cells = sheet.Worksheet.Descendants<S.Cell>().Where(cell => cell.CellValue is not null || cell.InlineString is not null).Select(cell => new
        {
            Address = cell.CellReference?.Value,
            Type = cell.DataType?.InnerText ?? "n",
            Value = cell.DataType?.Value == S.CellValues.SharedString ? shared[int.Parse(cell.CellValue!.Text, CultureInfo.InvariantCulture)] : cell.InlineString?.InnerText ?? cell.CellValue?.Text,
        }).ToArray(),
        Names = workbook.Workbook.DefinedNames?.Elements<S.DefinedName>().Select(name => new { Name = name.Name?.Value, Value = name.Text, Scope = name.LocalSheetId?.Value }).ToArray(),
        Links = sheet.Worksheet.Descendants<S.Hyperlink>().Select(link => new { Source = link.Reference?.Value, Location = link.Location?.Value, External = link.Id is null ? null : sheet.HyperlinkRelationships.Single(item => item.Id == link.Id.Value).Uri.OriginalString }).ToArray(),
        Tables = sheet.TableDefinitionParts.Select(part => part.Table.Reference?.Value).ToArray(),
        Filter = sheet.Worksheet.GetFirstChild<S.AutoFilter>()?.Reference?.Value,
        Conditional = sheet.Worksheet.Elements<S.ConditionalFormatting>().Select(rule => rule.SequenceOfReferences?.InnerText).ToArray(),
        Validation = sheet.Worksheet.Descendants<S.DataValidation>().Select(rule => rule.SequenceOfReferences?.InnerText).ToArray(),
        Comments = sheet.WorksheetCommentsPart?.Comments?.Descendants<S.Comment>().Select(comment => new { Reference = comment.Reference?.Value, Text = comment.InnerText }).ToArray(),
        DrawingRows = sheet.DrawingsPart?.WorksheetDrawing?.Descendants<Xdr.RowId>().Select(row => row.Text).ToArray(),
        Images = sheet.DrawingsPart?.ImageParts.Select(part => { using var source = part.GetStream(); return Convert.ToHexString(SHA256.HashData(source)); }).ToArray(),
    };
}
