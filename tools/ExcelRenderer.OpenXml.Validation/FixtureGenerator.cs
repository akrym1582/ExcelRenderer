using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using S = DocumentFormat.OpenXml.Spreadsheet;

/// <summary>Generates benchmark fixtures outside all measured processes.</summary>
internal static class FixtureGenerator
{
    /// <summary>Writes six controlled benchmark packages with the SDK streaming writer.</summary>
    /// <param name="output">Destination directory.</param>
    internal static void Run(string output)
    {
        Directory.CreateDirectory(output);
        foreach (var kind in new[] { "ordinary", "shared", "unique", "styled-empty", "sparse", "selection" })
        {
            using var doc = SpreadsheetDocument.Create(Path.Combine(output, kind + ".xlsx"), SpreadsheetDocumentType.Workbook);
            var wb = doc.AddWorkbookPart();
            var style = wb.AddNewPart<WorkbookStylesPart>();
            style.Stylesheet = new S.Stylesheet(
                new S.Fonts(new S.Font(new S.FontSize { Val = 11 }, new S.FontName { Val = "Calibri" })),
                new S.Fills(new S.Fill(new S.PatternFill { PatternType = S.PatternValues.None }), new S.Fill(new S.PatternFill { PatternType = S.PatternValues.Gray125 })),
                new S.Borders(new S.Border()),
                new S.CellStyleFormats(new S.CellFormat()),
                new S.CellFormats(new S.CellFormat(), new S.CellFormat { FontId = 0, FillId = 0, BorderId = 0, NumberFormatId = kind == "styled-empty" ? 2u : 0u }),
                new S.CellStyles(new S.CellStyle { Name = "Normal", BuiltinId = 0, FormatId = 0 }));
            if (kind == "shared" || kind == "unique")
            {
                var sst = wb.AddNewPart<SharedStringTablePart>();
                using var writer = OpenXmlWriter.Create(sst);
                writer.WriteStartElement(new S.SharedStringTable());
                var items = kind == "shared" ? 10 : 100000;
                for (var i = 0; i < items; i++)
                {
                    writer.WriteElement(new S.SharedStringItem(new S.Text("value-" + i)));
                }

                writer.WriteEndElement();
            }

            wb.Workbook = new S.Workbook(new S.Sheets());
            AddSheet(wb, kind == "selection" ? "Large" : "Data", kind, kind == "sparse" ? 2 : 100000);
            if (kind == "selection")
            {
                AddSheet(wb, "Small", "ordinary", 10);
            }

            wb.Workbook.Save();
            Console.WriteLine(kind);
        }
    }

    private static void AddSheet(WorkbookPart wb, string name, string kind, int count)
    {
        var part = wb.AddNewPart<WorksheetPart>();
        var id = (uint)(wb.Workbook.Sheets!.Count() + 1);
        wb.Workbook.Sheets!.Append(new S.Sheet { Id = wb.GetIdOfPart(part), SheetId = id, Name = name });
        using var writer = OpenXmlWriter.Create(part);
        writer.WriteStartElement(new S.Worksheet());
        writer.WriteElement(new S.SheetDimension { Reference = "A1:XFD1048576" });
        writer.WriteStartElement(new S.SheetData());
        var previousRow = 0;
        for (var i = 0; i < count; i++)
        {
            var row = kind == "sparse" && i == 1 ? 1048576 : (i / 100) + 1;
            var col = kind == "sparse" && i == 1 ? 16384 : (i % 100) + 1;
            if (row != previousRow)
            {
                if (previousRow > 0)
                {
                    writer.WriteEndElement();
                }

                writer.WriteStartElement(new S.Row { RowIndex = (uint)row });
                previousRow = row;
            }

            var address = new ExcelRenderer.OpenXml.Reader.Model.Cells.CellAddress(row, col).ToString();
            var cell = new S.Cell { CellReference = address, StyleIndex = 1 };
            if (kind != "styled-empty")
            {
                cell.CellValue = new S.CellValue((kind == "shared" ? i % 10 : i).ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (kind == "shared" || kind == "unique")
                {
                    cell.DataType = S.CellValues.SharedString;
                }
            }

            writer.WriteElement(cell);
        }

        if (previousRow > 0)
        {
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteEndElement();
    }
}
