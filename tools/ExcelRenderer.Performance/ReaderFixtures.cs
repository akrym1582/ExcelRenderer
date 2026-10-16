using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using S = DocumentFormat.OpenXml.Spreadsheet;

/// <summary>Creates reproducible reader and partial-format fixtures using SDK elements.</summary>
internal static class ReaderFixtures
{
    /// <summary>Writes all six benchmark cases and a small visual specimen.</summary>
    /// <param name="directory">The output directory.</param>
    internal static void Generate(string directory)
    {
        Directory.CreateDirectory(directory);
        foreach (var name in new[] { "hyperlinks", "shared", "unique", "rich-repeated", "rich-unique", "ordinary", "representative" })
        {
            using var package = SpreadsheetDocument.Create(Path.Combine(directory, name + ".xlsx"), SpreadsheetDocumentType.Workbook);
            var workbook = package.AddWorkbookPart();
            workbook.Workbook = new S.Workbook(new S.Sheets());
            var styles = FixtureWorkbook.Styles();
            workbook.AddNewPart<WorkbookStylesPart>().Stylesheet = styles;
            var strings = workbook.AddNewPart<SharedStringTablePart>();
            strings.SharedStringTable = new S.SharedStringTable();
            var count = name == "representative" ? 8 : name == "rich-unique" ? 1000 : 10000;
            if (name is "shared" or "ordinary" or "unique")
            {
                strings.SharedStringTable.Append(new S.SharedStringItem(new S.Text("Report ABC 123")));
            }
            else if (name is "rich-repeated" or "representative")
            {
                strings.SharedStringTable.Append(Rich("日本語 "));
            }

            var data = new S.SheetData();
            for (var row = 1; row <= count; row++)
            {
                var cell = new S.Cell { CellReference = "A" + row, StyleIndex = 0 };
                if (name == "hyperlinks")
                {
                    cell.CellFormula = new S.CellFormula("HYPERLINK(\"https://example.invalid/\",42)");
                    cell.CellValue = new S.CellValue("42");
                }
                else if (name.StartsWith("rich-", StringComparison.Ordinal) || name == "representative")
                {
                    cell.DataType = S.CellValues.SharedString;
                    cell.StyleIndex = 1;
                    styles.CellFormats!.Elements<S.CellFormat>().ElementAt(1).Alignment!.WrapText = true;
                    if (name == "rich-unique")
                    {
                        cell.CellValue = new S.CellValue(strings.SharedStringTable.ChildElements.Count.ToString(CultureInfo.InvariantCulture));
                        strings.SharedStringTable.Append(Rich(row.ToString(CultureInfo.InvariantCulture)));
                    }
                    else
                    {
                        cell.CellValue = new S.CellValue("0");
                    }
                }
                else
                {
                    cell.DataType = row % 3 == 1 ? S.CellValues.SharedString : row % 3 == 2 ? S.CellValues.Boolean : S.CellValues.Number;
                    cell.CellValue = new S.CellValue(row % 3 == 0 ? "42" : "0");
                    if (name == "unique")
                    {
                        styles.NumberingFormats ??= styles.InsertAt(new S.NumberingFormats(), 0);
                        styles.NumberingFormats.Append(new S.NumberingFormat { NumberFormatId = (uint)row + 164, FormatCode = "0\"-" + row + "\"" });
                        styles.CellFormats!.Append(new S.CellFormat { NumberFormatId = (uint)row + 164, ApplyNumberFormat = true });
                        cell.StyleIndex = (uint)styles.CellFormats.ChildElements.Count - 1;
                    }
                }

                data.Append(new S.Row(cell) { RowIndex = (uint)row });
            }

            FixtureWorkbook.Sheet(workbook, "Synthetic", new S.Worksheet(new S.SheetFormatProperties { DefaultRowHeight = 30 }, new S.Columns(new S.Column { Min = 1, Max = 1, Width = 35.710625, CustomWidth = true }), data));
        }
    }

    private static S.SharedStringItem Rich(string middle) => new(
        new S.Run(new S.RunProperties(new S.Bold(), new S.Color { Rgb = "FFFF0000" }), new S.Text("Alpha ") { Space = SpaceProcessingModeValues.Preserve }),
        new S.Run(new S.RunProperties(new S.Italic(), new S.FontSize { Val = 16 }), new S.Text(middle) { Space = SpaceProcessingModeValues.Preserve }),
        new S.Run(new S.RunProperties(new S.Underline { Val = S.UnderlineValues.Single }, new S.Color { Rgb = "FF0000FF" }), new S.Text("e\u0301 end")));
}
