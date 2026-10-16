using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml;
using ExcelRenderer.Excel;

/// <summary>Builds deterministic compact OOXML fixtures without an external spreadsheet application.</summary>
internal static class WorkbookFixture
{
    /// <summary>Creates numeric data, dependent formulas and a final SUM.</summary>
    /// <param name="count">Number of detail rows.</param>
    /// <param name="mapping">Whether to produce a one-row Mapping template.</param>
    /// <returns>Workbook package bytes.</returns>
    internal static byte[] Create(int count, bool mapping)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            Write(archive, "[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>");
            Write(archive, "_rels/.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            Write(archive, "xl/workbook.xml", "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Data\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            Write(archive, "xl/_rels/workbook.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
            using var output = archive.CreateEntry("xl/worksheets/sheet1.xml").Open();
            using var writer = XmlWriter.Create(output);
            writer.WriteStartElement("worksheet", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
            writer.WriteStartElement("sheetData");
            var rows = mapping ? 1 : count;
            for (var row = 1; row <= rows; row++)
            {
                writer.WriteStartElement("row");
                writer.WriteAttributeString("r", row.ToString(CultureInfo.InvariantCulture));
                writer.WriteStartElement("c");
                writer.WriteAttributeString("r", "A" + row);
                if (mapping)
                {
                    writer.WriteAttributeString("t", "inlineStr");
                    writer.WriteStartElement("is");
                    writer.WriteElementString("t", "**Items[*].Quantity");
                    writer.WriteEndElement();
                }
                else
                {
                    writer.WriteElementString("v", "2");
                }

                writer.WriteEndElement();
                writer.WriteStartElement("c");
                writer.WriteAttributeString("r", "B" + row);
                writer.WriteElementString("f", "ROUND(A" + row + "*10*1.1,2)");
                writer.WriteElementString("v", "22");
                writer.WriteEndElement();
                writer.WriteEndElement();
            }

            writer.WriteStartElement("row");
            writer.WriteAttributeString("r", (rows + 1).ToString(CultureInfo.InvariantCulture));
            writer.WriteStartElement("c");
            writer.WriteAttributeString("r", "B" + (rows + 1));
            writer.WriteElementString("f", "SUM(B1:B" + rows + ")");
            writer.WriteElementString("v", (22 * rows).ToString(CultureInfo.InvariantCulture));
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        return stream.ToArray();
    }

    private static void Write(ZipArchive archive, string name, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open());
        writer.Write(content);
    }
}
