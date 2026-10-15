using System.IO.Compression;
using System.Xml.Linq;
using ClosedXML.Excel;

/// <summary>Creates content-free reproducible reader and partial-format fixtures.</summary>
internal static class ReaderFixtures
{
    /// <summary>Writes all six benchmark cases and a small visual specimen.</summary>
    /// <param name="directory">The output directory.</param>
    internal static void Generate(string directory)
    {
        Directory.CreateDirectory(directory);
        foreach (var name in new[] { "hyperlinks", "shared", "unique", "rich-repeated", "rich-unique", "ordinary", "representative" })
        {
            using var workbook = new XLWorkbook();
            var sheet = workbook.AddWorksheet("Synthetic");
            sheet.Column(1).Width = 35;
            sheet.RowHeight = 30;
            var rows = name == "representative" ? 8 : name == "rich-unique" ? 1000 : 10000;
            sheet.Range(1, 1, rows, 1).Style.Font.FontName = "Noto Sans JP";
            sheet.Range(1, 1, rows, 1).Style.Font.FontSize = 10;
            for (var row = 1; row <= rows; row++)
            {
                var cell = sheet.Cell(row, 1);
                if (name == "hyperlinks")
                {
                    cell.FormulaA1 = "HYPERLINK(\"https://example.invalid/\",42)";
                }
                else if (name.StartsWith("rich-", StringComparison.Ordinal) || name == "representative")
                {
                    var rich = cell.CreateRichText();
                    rich.AddText("Alpha ").SetFontColor(XLColor.Red).SetBold();
                    rich.AddText(name == "rich-unique" ? row.ToString(System.Globalization.CultureInfo.InvariantCulture) : "日本語 ").SetFontSize(16).SetItalic();
                    rich.AddText("e\u0301 end").SetUnderline(XLFontUnderlineValues.Single).SetFontColor(XLColor.Blue);
                    cell.Style.Alignment.WrapText = true;
                }
                else
                {
                    cell.Value = (row % 3) switch { 0 => (XLCellValue)42, 1 => "Report ABC 123", _ => false };
                    if (name == "unique")
                    {
                        cell.Style.NumberFormat.Format = "0\"-" + row + "\"";
                    }
                }
            }

            var path = Path.Combine(directory, name + ".xlsx");
            workbook.SaveAs(path);
            if (name == "hyperlinks")
            {
                using var zip = ZipFile.Open(path, ZipArchiveMode.Update);
                var entry = zip.GetEntry("xl/worksheets/sheet1.xml")!;
                XDocument document;
                using (var stream = entry.Open())
                {
                    document = XDocument.Load(stream);
                }

                XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                foreach (var cell in document.Descendants(ns + "c"))
                {
                    cell.Element(ns + "v")?.Remove();
                    cell.Add(new XElement(ns + "v", "42"));
                }

                entry.Delete();
                using var output = zip.CreateEntry("xl/worksheets/sheet1.xml").Open();
                document.Save(output);
            }
        }
    }
}
