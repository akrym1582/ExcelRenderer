using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.TestFixtures;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.Mapping.Tests;

internal static class MappingFixture
{
    internal static MemoryStream Fixture(string name, params (string Address, string Value)[] changes)
    {
        var stream = RetainedWorkbook.Open(name);
        if (changes.Length == 0) return stream;
        using (var document = SpreadsheetDocument.Open(stream, true))
        {
            var part = document.WorkbookPart!.WorksheetParts.First();
            foreach (var (address, value) in changes)
            {
                var cell = part.Worksheet.Descendants<S.Cell>().Single(cell => cell.CellReference == address);
                cell.RemoveAllChildren();
                cell.DataType = S.CellValues.InlineString;
                cell.InlineString = new S.InlineString(new S.Text(value));
            }
        }
        stream.Position = 0;
        return stream;
    }

}
