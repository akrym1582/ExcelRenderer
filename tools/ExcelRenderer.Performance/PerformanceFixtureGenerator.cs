using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.OpenXml;
using S = DocumentFormat.OpenXml.Spreadsheet;

/// <summary>Generates rendering benchmark fixtures without a workbook editing library.</summary>
internal static class PerformanceFixtureGenerator
{
    /// <summary>Writes a controlled grid and mode-specific geometry.</summary>
    /// <param name="path">Destination workbook.</param>
    /// <param name="rows">Number of body rows.</param>
    /// <param name="mode">Benchmark fixture kind.</param>
    internal static void Generate(string path, int rows, string mode)
    {
        using var package = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var workbook = package.AddWorkbookPart();
        workbook.Workbook = new S.Workbook(new S.Sheets());
        var styles = FixtureWorkbook.Styles(mode == "bold", mode == "wrap");
        var borders = styles.Borders!;
        var formats = styles.CellFormats!;
        for (var vertical = 0; vertical < 3; vertical++)
        {
            for (var horizontal = 0; horizontal < 3; horizontal++)
            {
                borders.Append(new S.Border(new S.LeftBorder { Style = horizontal == 0 ? S.BorderStyleValues.Double : S.BorderStyleValues.Thin }, new S.RightBorder { Style = horizontal == 2 ? S.BorderStyleValues.Double : S.BorderStyleValues.Thin }, new S.TopBorder { Style = vertical == 0 ? S.BorderStyleValues.Double : S.BorderStyleValues.Thin }, new S.BottomBorder { Style = vertical == 2 ? S.BorderStyleValues.Double : S.BorderStyleValues.Thin }));
                formats.Append(new S.CellFormat(new S.Alignment { WrapText = mode == "wrap" }) { FillId = 2, BorderId = (uint)borders.ChildElements.Count - 1, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true });
            }
        }

        foreach (var color in new[] { "FFFFFF00", "FFADD8E6", "FFFF0000" })
        {
            styles.Fills!.Append(new S.Fill(new S.PatternFill(new S.ForegroundColor { Rgb = color }, new S.BackgroundColor { Indexed = 64 }) { PatternType = S.PatternValues.Solid }));
            formats.Append(new S.CellFormat { FillId = (uint)styles.Fills.ChildElements.Count - 1, ApplyFill = true });
        }

        workbook.AddNewPart<WorkbookStylesPart>().Stylesheet = styles;
        var data = new S.SheetData();
        var columns = new S.Columns(new S.Column { Min = 1, Max = 20, Width = 10.710625, CustomWidth = true });
        var worksheet = new S.Worksheet(columns, data);
        for (var row = 1; row <= rows; row++)
        {
            var record = new S.Row { RowIndex = (uint)row, Height = 18, CustomHeight = true };
            for (var column = 1; column <= 20; column++)
            {
                var style = (uint)(2 + ((row == 1 ? 0 : row == rows ? 2 : 1) * 3) + (column == 1 ? 0 : column == 20 ? 2 : 1));
                var cell = mode == "empty" ? new S.Cell { CellReference = new CellAddress(row, column).ToString(), StyleIndex = style } : FixtureWorkbook.Text(row, column, mode == "latin" ? "Report ABC 123" : "帳票 ABC 123", style);
                if (mode == "wrap" && row == 1 && column == 1)
                {
                    cell.InlineString = new S.InlineString(new S.Text(string.Concat(Enumerable.Repeat("折り返し ABC e\u0301 葛\U000E0100 ", 30))));
                }

                record.Append(cell);
            }

            data.Append(record);
        }

        if (mode == "rowstyle")
        {
            columns.Append(new S.Column { Min = 30, Max = 30, Style = 12 });
            data.Append(new S.Row { RowIndex = (uint)(rows + 10000), StyleIndex = 11, CustomFormat = true });
        }

        if (mode == "far")
        {
            data.Append(new S.Row(new S.Cell { CellReference = "AD" + (rows + 10000).ToString(CultureInfo.InvariantCulture), StyleIndex = 13 }) { RowIndex = (uint)(rows + 10000) });
            data.Append(new S.Row { RowIndex = (uint)(rows + 20000), Height = 25, CustomHeight = true });
        }

        if (mode == "merges")
        {
            worksheet.Append(new S.MergeCells(Enumerable.Range(0, (rows + 1) / 2).Select(index => new S.MergeCell { Reference = "A" + ((index * 2) + 1) + ":B" + Math.Min((index * 2) + 2, rows) })));
        }

        var part = FixtureWorkbook.Sheet(workbook, "Synthetic", worksheet);
        workbook.Workbook.DefinedNames = new S.DefinedNames(new S.DefinedName("'Synthetic'!$A$1:$T$" + rows) { Name = "_xlnm.Print_Area", LocalSheetId = 0 });
        if (mode is "merges" or "mixed")
        {
            workbook.Workbook.DefinedNames.Append(new S.DefinedName("'Synthetic'!$1:$2") { Name = "_xlnm.Print_Titles", LocalSheetId = 0 });
        }

        if (mode == "mixed")
        {
            workbook.Workbook.DefinedNames.Elements<S.DefinedName>().First().Text = "'Synthetic'!$A$1:$T$" + rows + ",'Synthetic'!$A$1:$D$10";
            worksheet.Append(new S.Hyperlinks(new S.Hyperlink { Reference = "A1", Location = "Other!A1" }));
            FixtureWorkbook.Sheet(workbook, "Other", new S.Worksheet(new S.SheetData(new S.Row(FixtureWorkbook.Text(1, 1, "リンク先")) { RowIndex = 1 }), new S.PageSetup { PaperSize = 8 }));
        }

        if (mode is "mixed" or "images")
        {
            using var bitmap = new SkiaSharp.SKBitmap(mode == "images" ? 1024 : 20, mode == "images" ? 1024 : 20);
            bitmap.Erase(SkiaSharp.SKColors.Blue);
            using var bytes = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            FixtureWorkbook.Image(part, bytes.ToArray(), mode == "images" ? 0 : 34, 100, mode == "images" ? rows * 24 : 250);
        }
    }
}
