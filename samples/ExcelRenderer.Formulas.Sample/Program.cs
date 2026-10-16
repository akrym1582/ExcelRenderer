using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer;
using ExcelRenderer.Excel;
using ExcelRenderer.Fonts;
using ExcelRenderer.Mapping;
using ExcelRenderer.Markdown;
using ExcelRenderer.OpenXml;
using S = DocumentFormat.OpenXml.Spreadsheet;

if (args.Length > 3)
{
    Console.Error.WriteLine("Usage: sample [new-output-directory] [line-count=3] [font.ttf]");
    return 1;
}

var directory = Path.GetFullPath(args.Length > 0 ? args[0] : "formula-invoice-output");
var count = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 3;
var fontPath = Path.GetFullPath(args.Length > 2 ? args[2] : "third_party/NotoSansJP/NotoSansJP-Regular.ttf");
if (count is < 0 or > 1000 || !File.Exists(fontPath))
{
    throw new ArgumentException("Use 0–1000 lines and an existing regular TrueType font.");
}

if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
{
    throw new IOException("Choose a new or empty output directory: " + directory);
}

Directory.CreateDirectory(directory);
var templatePath = Path.Combine(directory, "template.xlsx");
var mappedPath = Path.Combine(directory, "invoice.xlsx");
CreateTemplate(templatePath);
var items = Enumerable.Range(1, count).Select(quantity => new { quantity, price = 10 }).ToArray();
ExcelTemplateMapper.Map(templatePath, mappedPath, new { items }, new MappingOptions
{
    FormulaCalculationMode = FormulaCalculationMode.Recalculate,
});

var expected = count * (count + 1) / 2 * 11;
using (var input = File.OpenRead(mappedPath))
using (var reader = OpenXmlWorkbookReader.Open(input))
{
    var total = reader.EnumerateCells(1).Single(cell => cell.Address.Row == count + 2 && cell.Address.Column == 4);
    if (total.Value.Number != expected)
    {
        throw new InvalidDataException($"Expected total {expected}; received {total.Value.Number}.");
    }
}

var fonts = new FontOptions { FontFiles = [fontPath], AllowSystemFonts = false, UseFontPack = false };
var read = new ExcelReadOptions { FormulaDisplayMode = FormulaDisplayMode.Recalculate };
await ExcelConverter.ConvertToPdfAsync(mappedPath, Path.Combine(directory, "invoice.pdf"), new PdfExportOptions { SheetName = "Invoice", FontOptions = fonts, ReadOptions = read });
await ExcelConverter.ConvertToImagesAsync(mappedPath, Path.Combine(directory, "png"), new ImageExportOptions { SheetName = "Invoice", FontOptions = fonts, ReadOptions = read });
await ExcelConverter.ConvertToSvgAsync(mappedPath, Path.Combine(directory, "svg"), new SvgExportOptions { SheetName = "Invoice", FontOptions = fonts, ReadOptions = read });
await ExcelConverter.ConvertToMarkdownAsync(mappedPath, Path.Combine(directory, "invoice.md"), new MarkdownExportOptions { SheetName = "Invoice", FontOptions = fonts, ReadOptions = read });
Console.WriteLine($"Verified {count} lines; total {expected}. XLSX, PDF, PNG, SVG and Markdown: {directory}");
return 0;

static void CreateTemplate(string path)
{
    using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
    var workbook = document.AddWorkbookPart();
    workbook.Workbook = new S.Workbook(new S.Sheets());
    var invoice = workbook.AddNewPart<WorksheetPart>();
    invoice.Worksheet = new S.Worksheet(
        new S.Columns(new S.Column { Min = 1, Max = 4, Width = 18, CustomWidth = true }),
        new S.SheetData(
            new S.Row(Text("A1", "Quantity"), Text("B1", "Unit price"), Text("C1", "Net"), Text("D1", "Gross")) { RowIndex = 1 },
            new S.Row(Text("A2", "**items[*].quantity"), Text("B2", "**items[*].price"), Formula("C2", "A2*B2"), Formula("D2", "ROUND(C2*(1+Rates!$A$1),0)")) { RowIndex = 2 },
            new S.Row(Text("A3", "Total"), Formula("D3", "SUM(Amounts)")) { RowIndex = 3 }));
    workbook.Workbook.Sheets!.Append(new S.Sheet { Id = workbook.GetIdOfPart(invoice), SheetId = 1, Name = "Invoice" });
    var rates = workbook.AddNewPart<WorksheetPart>();
    rates.Worksheet = new S.Worksheet(new S.SheetData(new S.Row(new S.Cell(new S.CellValue("0.1")) { CellReference = "A1" }) { RowIndex = 1 }));
    workbook.Workbook.Sheets.Append(new S.Sheet { Id = workbook.GetIdOfPart(rates), SheetId = 2, Name = "Rates" });
    workbook.Workbook.DefinedNames = new S.DefinedNames(new S.DefinedName("Invoice!$D$1:$D$2") { Name = "Amounts" });
    workbook.AddNewPart<WorkbookStylesPart>().Stylesheet = new S.Stylesheet(
        new S.Fonts(new S.Font(new S.FontSize { Val = 11 }, new S.FontName { Val = "Noto Sans JP" })),
        new S.Fills(new S.Fill(new S.PatternFill { PatternType = S.PatternValues.None }), new S.Fill(new S.PatternFill { PatternType = S.PatternValues.Gray125 })),
        new S.Borders(new S.Border()),
        new S.CellStyleFormats(new S.CellFormat()),
        new S.CellFormats(new S.CellFormat()),
        new S.CellStyles(new S.CellStyle { Name = "Normal", FormatId = 0, BuiltinId = 0 }));
}

static S.Cell Text(string address, string value) => new(new S.InlineString(new S.Text(value))) { CellReference = address, DataType = S.CellValues.InlineString };

static S.Cell Formula(string address, string value) => new(new S.CellFormula(value), new S.CellValue("999")) { CellReference = address };
