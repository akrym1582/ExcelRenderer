using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;

namespace ExcelRenderer.OpenXml.Tests;

internal static class Fixture
{
    internal const string Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    internal const string Styles = """
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
        <fonts count="2"><font><name val="Calibri"/><sz val="11"/></font><font><name val="Arial"/><b/></font></fonts>
        <fills count="1"><fill><patternFill patternType="none"/></fill></fills>
        <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
        <cellStyleXfs count="1"><xf fontId="0" fillId="0" borderId="0" numFmtId="0"/></cellStyleXfs>
        <cellXfs count="3"><xf fontId="0" fillId="0" borderId="0" numFmtId="0"/><xf fontId="1" numFmtId="2" xfId="0" applyFont="0"/><xf fontId="0" numFmtId="9"/></cellXfs>
        <cellStyles count="1"><cellStyle name="標準" builtinId="0" xfId="0"/></cellStyles></styleSheet>
        """;

    internal static MemoryStream Create(string body, string? styles = null, string? sst = null, string? definitions = null, bool date1904 = false, string? secondBody = null, bool chartFirst = false, string name = "Sheet1", string? theme = null)
    {
        var stream = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, true))
        {
            var wb = doc.AddWorkbookPart();
            var sheets = new StringBuilder();
            if (chartFirst)
            {
                var chart = wb.AddNewPart<ChartsheetPart>("rIdChart");
                Feed(chart, $"<chartsheet xmlns=\"{Ns}\"/>");
                sheets.Append("<sheet name=\"Chart\" sheetId=\"20\" r:id=\"rIdChart\"/>");
            }

            var first = wb.AddNewPart<WorksheetPart>("rId1");
            Feed(first, Wrap(body));
            sheets.Append($"<sheet name=\"{System.Security.SecurityElement.Escape(name)}\" sheetId=\"42\" r:id=\"rId1\" state=\"hidden\"/>");
            if (secondBody != null)
            {
                var second = wb.AddNewPart<WorksheetPart>("rId2");
                Feed(second, Wrap(secondBody));
                sheets.Append("<sheet name=\"Second\" sheetId=\"99\" r:id=\"rId2\"/>");
            }

            if (styles != null)
            {
                Feed(wb.AddNewPart<WorkbookStylesPart>(), styles);
            }

            if (sst != null)
            {
                Feed(wb.AddNewPart<SharedStringTablePart>(), $"<sst xmlns=\"{Ns}\">{sst}</sst>");
            }

            if (theme != null)
            {
                Feed(wb.AddNewPart<ThemePart>(), theme);
            }

            Feed(wb, $"<workbook xmlns=\"{Ns}\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><workbookPr date1904=\"{(date1904 ? 1 : 0)}\"/><sheets>{sheets}</sheets>{(definitions == null ? "" : "<definedNames>" + definitions + "</definedNames>")}</workbook>");
        }

        stream.Position = 0;
        return stream;
    }

    internal static string Cells(string cells) => "<sheetData><row r=\"1\">" + cells + "</row></sheetData>";

    private static string Wrap(string body) => $"<worksheet xmlns=\"{Ns}\">{body}</worksheet>";

    private static void Feed(OpenXmlPart part, string xml)
    {
        using var data = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        part.FeedData(data);
    }
}
