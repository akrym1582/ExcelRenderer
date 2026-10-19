using System.Runtime.CompilerServices;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.Formulas.Data;
using ExcelRenderer.Formulas.Evaluation;
using ExcelRenderer.Formulas.References;
using ExcelRenderer.Formulas.Values;
using ExcelRenderer.OpenXml.Reader;
using S = DocumentFormat.OpenXml.Spreadsheet;

// Separate methods let the compatibility check exercise each old type reference.
if (args.Length == 0 || args[0] == "engine")
{
    ShowEngineIdentity();
    EvaluateFormula();
}

if (args.Length == 0 || args[0] == "reader")
{
    ShowReaderIdentity();
    ReadWorkbook();
}

[MethodImpl(MethodImplOptions.NoInlining)]
static void ShowEngineIdentity() => Console.WriteLine(typeof(FormulaEngine).FullName);

[MethodImpl(MethodImplOptions.NoInlining)]
static void ShowReaderIdentity() => Console.WriteLine(typeof(OpenXmlWorkbookReader).FullName);

[MethodImpl(MethodImplOptions.NoInlining)]
static void EvaluateFormula()
{
    var address = new FormulaCellAddress("Invoice", 1, 1);
    var source = new InMemoryFormulaWorkbook.Builder()
        .AddSheet("Invoice")
        .SetValue(address, FormulaValue.FromNumber(3))
        .SetFormula(new FormulaCellAddress("Invoice", 1, 2), "A1*125")
        .Build();
    var result = new FormulaEngine().CreateSession(source)
        .EvaluateCell(new FormulaCellAddress("Invoice", 1, 2));
    if (!result.Success || result.Value.Number != 375)
    {
        throw new InvalidOperationException("Expected formula result 375.");
    }

    Console.WriteLine("engine: 375");
}

[MethodImpl(MethodImplOptions.NoInlining)]
static void ReadWorkbook()
{
    using var input = new MemoryStream();
    using (var document = SpreadsheetDocument.Create(input, SpreadsheetDocumentType.Workbook, true))
    {
        var workbook = document.AddWorkbookPart();
        workbook.Workbook = new S.Workbook();
        var sheet = workbook.AddNewPart<WorksheetPart>();
        sheet.Worksheet = new S.Worksheet(new S.SheetData(new S.Row(
            new S.Cell { CellReference = "A1", CellValue = new S.CellValue("375") }) { RowIndex = 1 }));
        workbook.Workbook.Append(new S.Sheets(new S.Sheet
        {
            Name = "Invoice",
            SheetId = 1,
            Id = workbook.GetIdOfPart(sheet),
        }));
        workbook.Workbook.Save();
    }

    input.Position = 0;
    using var reader = OpenXmlWorkbookReader.Open(input);
    var cell = reader.EnumerateCells(reader.SelectedWorksheetIndices[0]).Single();
    if (cell.Value.Number != 375)
    {
        throw new InvalidOperationException("Expected cell value 375.");
    }

    Console.WriteLine("reader: 375");
}
