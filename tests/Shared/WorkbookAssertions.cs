using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.OpenXml.Reader;
using ExcelRenderer.OpenXml.Reader.Model.Cells;
using ExcelRenderer.OpenXml.Reader.Model.Styles;
using ExcelRenderer.OpenXml.Reader.Model.Workbook;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.TestFixtures;

/// <summary>Owns independent SDK and streaming views for assertions on saved workbooks.</summary>
internal sealed class WorkbookProbe : IDisposable
{
    private readonly Dictionary<int, Dictionary<string, CellRecord>> cells = new();
    private readonly MemoryStream xmlInput;
    private readonly MemoryStream readerInput;
    private WorkbookProbe(byte[] bytes)
    {
        xmlInput = new(bytes); readerInput = new(bytes);
        Document = SpreadsheetDocument.Open(xmlInput, false);
        Reader = OpenXmlWorkbookReader.Open(readerInput);
    }
    internal SpreadsheetDocument Document { get; }
    internal OpenXmlWorkbookReader Reader { get; }
    internal IReadOnlyDictionary<string, CellRecord> CellRecords(int sheet) { if (!cells.TryGetValue(sheet, out var values)) cells.Add(sheet, values = Reader.EnumerateCells(sheet).ToDictionary(cell => cell.Address.ToString()));return values; }
    internal static WorkbookProbe Open(Stream input) { input.Position = 0;using var copy = new MemoryStream();input.CopyTo(copy);return new(copy.ToArray()); }
    internal static WorkbookProbe Open(string path) => new(File.ReadAllBytes(path));
    public void Dispose() { Reader.Dispose();Document.Dispose();readerInput.Dispose();xmlInput.Dispose(); }
}

internal static class WorkbookAssertions
{
    internal static CellRecord Cell(WorkbookProbe book, string address, int sheet = 1) => book.CellRecords(sheet)[address];
    internal static CellRecord Cell(WorkbookProbe book, int row, int column, int sheet = 1) => Cell(book, new CellAddress(row, column).ToString(), sheet);
    internal static string Text(WorkbookProbe book, string address, int sheet = 1) => Cell(book, address, sheet).Value.Text ?? string.Empty;
    internal static string Text(WorkbookProbe book, int row, int column) => Text(book, new CellAddress(row,column).ToString());
    internal static double Number(WorkbookProbe book, string address, int sheet = 1) => Cell(book, address, sheet).Value.Number!.Value;
    internal static double Number(WorkbookProbe book, int row, int column) => Number(book, new CellAddress(row,column).ToString());
    internal static CellStyleInfo Style(WorkbookProbe book, string address) => book.Reader.ResolveStyle(Cell(book, address));
    internal static CellStyleInfo Style(WorkbookProbe book, int row, int column) => Style(book, new CellAddress(row,column).ToString());
    internal static RowInfo Row(WorkbookProbe book, int row) => Info(book).Rows.Single(item => item.Index == row);
    internal static int LastContentRow(WorkbookProbe book) => Cells(book).Where(cell => cell.Value.Type != CellValueType.Blank || cell.Formula != null).Select(cell => cell.Address.Row).DefaultIfEmpty(0).Max();
    internal static WorksheetInfo Info(WorkbookProbe book) => book.Reader.ReadWorksheetInfo(1);
    internal static IEnumerable<DefinedNameInfo> Names(WorkbookProbe book) => book.Reader.Workbook.DefinedNames;
    internal static IEnumerable<CellRecord> Cells(WorkbookProbe book) => book.CellRecords(1).Values;
    internal static IEnumerable<int> Breaks(WorkbookProbe book, bool columns = false) => (columns ? Info(book).ColumnBreaks : Info(book).RowBreaks).Select(element => int.Parse(element.Attributes["id"],CultureInfo.InvariantCulture));
    internal static string PrintArea(WorkbookProbe book) { var area = Info(book).PrintReferences.Single(item => item.Name == "_xlnm.Print_Area").Ranges.Single();return new CellAddress(area.FirstRow,area.FirstColumn)+":"+new CellAddress(area.LastRow,area.LastColumn); }
    internal static DateTime Date(WorkbookProbe book, string address) => DateTime.FromOADate(Number(book,address) + (book.Reader.Workbook.Date1904 ? 1462 : 0));
    internal static TimeSpan Duration(WorkbookProbe book, string address) => TimeSpan.FromTicks((long)Math.Round(Number(book,address) * TimeSpan.TicksPerDay));
    internal static string Display(WorkbookProbe book, int row, int column) => book.Reader.FormatCell(Cell(book,row,column)).Text;
    internal static S.Worksheet Xml(WorkbookProbe book) => book.Document.WorkbookPart!.WorksheetParts.First().Worksheet;
    internal static string? Fill(WorkbookProbe book, string address) => Style(book,address).Fill.Children.Single().Children.FirstOrDefault()?.Attributes.GetValueOrDefault("rgb");
    internal static string? Fill(WorkbookProbe book, int row, int column) => Fill(book,new CellAddress(row,column).ToString());
}
