using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using S = DocumentFormat.OpenXml.Spreadsheet;

/// <summary>Creates selection and missing-glyph workbooks using the existing Open XML SDK.</summary>
internal static class WorkbookFixtures
{
    /// <summary>Writes reproducible inputs without an Excel installation.</summary>
    /// <param name="directory">The output directory.</param>
    internal static void Create(string directory)
    {
        using (var document = SpreadsheetDocument.Create(Path.Combine(directory, "pages.xlsx"), SpreadsheetDocumentType.Workbook))
        {
            var workbook = document.AddWorkbookPart();
            workbook.Workbook = new(new S.Sheets());
            var first = new S.Worksheet(
                new S.SheetData(new S.Row(Text("A1", "First sheet")) { RowIndex = 1 }, new S.Row(Text("A20", "Page two")) { RowIndex = 20 }),
                new S.Hyperlinks(new S.Hyperlink { Reference = "A2", Location = "'Second'!A1", Tooltip = "cross sheet" }),
                new S.PageSetup { PaperSize = 9 },
                new S.RowBreaks(new S.Break { Id = 18, ManualPageBreak = true, Max = 16383 }));
            Add(workbook, first, "First");
            var second = new S.Worksheet(
                new S.SheetData(new S.Row(Text("A1", "Second sheet")) { RowIndex = 1 }),
                new S.Hyperlinks(new S.Hyperlink { Reference = "A2", Location = "'First'!A20", Tooltip = "excluded target" }),
                new S.PageSetup { PaperSize = 1, Orientation = S.OrientationValues.Landscape },
                new S.HeaderFooter(new S.OddHeader("&L&D &T &P / &N")));
            Add(workbook, second, "Second");
            workbook.Workbook.Append(new S.DefinedNames(new S.DefinedName("'First'!$A$1:$C$30") { Name = "_xlnm.Print_Area", LocalSheetId = 0 }));
        }

        using (var document = SpreadsheetDocument.Create(Path.Combine(directory, "missing.xlsx"), SpreadsheetDocumentType.Workbook))
        {
            var workbook = document.AddWorkbookPart();
            workbook.Workbook = new(new S.Sheets());
            Add(workbook, new(new S.SheetData(new S.Row(Text("A1", "missing \uE000")) { RowIndex = 1 })), "missing");
        }
    }

    private static void Add(WorkbookPart workbook, S.Worksheet worksheet, string name)
    {
        var part = workbook.AddNewPart<WorksheetPart>();
        part.Worksheet = worksheet;
        workbook.Workbook.Sheets!.Append(new S.Sheet { Id = workbook.GetIdOfPart(part), SheetId = (uint)workbook.Workbook.Sheets.ChildElements.Count + 1, Name = name });
    }

    private static S.Cell Text(string address, string value) => new(new S.InlineString(new S.Text(value) { Space = SpaceProcessingModeValues.Preserve }))
    {
        CellReference = address,
        DataType = S.CellValues.InlineString,
    };
}
