using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer;
using ExcelRenderer.Excel;
using ExcelRenderer.Fonts;
using ExcelRenderer.Mapping;
using ExcelRenderer.Mapping.Configuration;
using ExcelRenderer.Markdown;
using ExcelRenderer.OpenXml.Calculation;
using ExcelRenderer.OpenXml.Reading;
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
var oracleInputPath = Path.Combine(directory, "invoice-input.xlsx");
CreateTemplate(templatePath);
var items = Enumerable.Range(1, count).Select(quantity => new { quantity, code = "001" }).ToArray();
ExcelTemplateMapper.Map(templatePath, oracleInputPath, new { items }, new MappingOptions
{
    FormulaCalculationMode = FormulaCalculationMode.Recalculate,
    RecalculateMappedWorkbook = false,
});
using (var input = File.OpenRead(oracleInputPath))
using (var reader = OpenXmlWorkbookReader.Open(input))
{
    var formulaCells = reader.EnumerateCells(1).Where(cell => cell.Formula != null).ToArray();
    if (formulaCells.Any(cell => cell.Formula!.HasCachedResult) || (count > 0 && formulaCells.Single(cell => cell.Address.Row == count + 1 && cell.Address.Column == 3).Formula!.RawFormula != $"(A{count + 1}*VLOOKUP(B{count + 1},'Master'!$A$1:$C$2,3,FALSE))"))
    {
        throw new InvalidDataException("Mapping must invalidate caches and rewrite copied detail references.");
    }
}

using (var input = File.OpenRead(oracleInputPath))
using (var output = File.Create(mappedPath))
{
    WorkbookRecalculator.Recalculate(input, output);
}

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
        new S.Columns(new S.Column { Min = 1, Max = 6, Width = 10, CustomWidth = true }),
        new S.SheetData(
            new S.Row(Text("A1", "Quantity"), Text("B1", "Code"), Text("C1", "Net"), Text("D1", "Gross"), Text("E1", "Name"), Text("F1", "Category")) { RowIndex = 1 },
            new S.Row(Text("A2", "**items[*].quantity"), Text("B2", "**items[*].code"), Formula("C2", "A2*VLOOKUP(B2,Master!$A$1:$C$2,3,FALSE)"), Formula("D2", "ROUND(C2*(1+Rates!$A$1),0)"), Formula("E2", "VLOOKUP(B2,Master!$A$1:$C$2,2,FALSE)"), Formula("F2", "IF(MOD(A2,2)=0,\"B\",\"A\")")) { RowIndex = 2 },
            new S.Row(Text("A3", "Total"), Formula("D3", "SUM(Amounts)")) { RowIndex = 3 },
            new S.Row(Text("A4", "Category A"), Formula("D4", "SUMIFS(Amounts,Categories,\"A\")"), Formula("E4", "COUNTIFS(Categories,\"A\")")) { RowIndex = 4 },
            new S.Row(Formula("A5", "DATE(2026,10,17)"), Formula("B5", "EOMONTH(A5,1)"), Formula("C5", "TEXT(A5,\"yyyy年mm月\")")) { RowIndex = 5 }));
    workbook.Workbook.Sheets!.Append(new S.Sheet { Id = workbook.GetIdOfPart(invoice), SheetId = 1, Name = "Invoice" });
    var rates = workbook.AddNewPart<WorksheetPart>();
    rates.Worksheet = new S.Worksheet(new S.SheetData(new S.Row(new S.Cell(new S.CellValue("0.1")) { CellReference = "A1" }) { RowIndex = 1 }));
    workbook.Workbook.Sheets.Append(new S.Sheet { Id = workbook.GetIdOfPart(rates), SheetId = 2, Name = "Rates" });
    var master = workbook.AddNewPart<WorksheetPart>();
    master.Worksheet = new S.Worksheet(new S.SheetData(
        new S.Row(Text("A1", "001"), Text("B1", "Item one"), new S.Cell(new S.CellValue("10")) { CellReference = "C1" }) { RowIndex = 1 },
        new S.Row(Text("A2", "002"), Text("B2", "Item two"), new S.Cell(new S.CellValue("25")) { CellReference = "C2" }) { RowIndex = 2 }));
    workbook.Workbook.Sheets.Append(new S.Sheet { Id = workbook.GetIdOfPart(master), SheetId = 3, Name = "Master" });
    workbook.Workbook.DefinedNames = new S.DefinedNames(new S.DefinedName("Invoice!$D$1:$D$2") { Name = "Amounts" }, new S.DefinedName("Invoice!$F$1:$F$2") { Name = "Categories" });
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
