using System.Xml.Linq;
using ExcelRenderer.OpenXml.Diagnostics;
using ExcelRenderer.OpenXml.Reading;
using ExcelRenderer.OpenXml.Writing;
using ExcelRenderer.OpenXml.Writing.Plans;
using Xunit;

namespace ExcelRenderer.OpenXml.Tests;

public sealed class RowPrototypeCacheTests
{
    [Fact]
    public void WriterRetainsOnlyRepeatedRowsAndDoesNotLeakEditedValues()
    {
        using var input = Fixture.Create("<sheetData><row r='1'><c r='A1' t='inlineStr'><is><t>header</t></is></c></row><row r='2'><c r='A2' t='inlineStr'><is><t>detail</t></is></c></row><row r='3'><c r='A3' t='inlineStr'><is><t>footer</t></is></c></row></sheetData>");
        using var output = new MemoryStream();
        var rows = new[]
        {
            new OutputRowOccurrence(1, 1, 0, 0, []),
            new OutputRowOccurrence(2, 2, 1, 0, [new CellValueEdit(1, "first")]),
            new OutputRowOccurrence(2, 3, 2, 0, []),
            new OutputRowOccurrence(3, 4, 0, 0, []),
        };
        var metrics = new Dictionary<string, long>();
        OperationMetrics.Observer = (name, value) => metrics[name] = value;
        try
        {
            OpenXmlWorkbookWriter.Write(input, output, new WorkbookEditPlan([new SheetEditPlan(1, 3, rows, [], [], false)]));
        }
        finally { OperationMetrics.Observer = null; }
        Assert.Equal(1, metrics["writer.rowCacheEntries"]);
        Assert.Equal(1, metrics["writer.rowCacheHits"]);
        output.Position = 0;
        using var reader = OpenXmlWorkbookReader.Open(output);
        Assert.Equal(new[] { "header", "first", "detail", "footer" }, reader.EnumerateCells(1).Select(cell => cell.Value.Text));
    }

    [Fact]
    public void DeepCopiesProtectPrototypesAcrossInterleavedRepeatedRows()
    {
        var cache = new RowPrototypeCache();
        for (var row = 1; row <= 4; row++)
        {
            var copy = cache.Store(row, 100, XElement.Parse($"<row r='{row}'><c><v>original{row}</v></c></row>"));
            copy.Element("c")!.Element("v")!.Value = "edited";
        }

        for (var repeat = 0; repeat < 10; repeat++)
        for (var row = 1; row <= 4; row++)
        {
            var copy = cache.Clone(row)!;
            Assert.Equal("original" + row, copy.Element("c")!.Value);
            copy.RemoveAll();
        }
        Assert.Equal(4, cache.Count);
    }

    [Fact]
    public void EntryByteAndIndividualRowLimitsBoundRetentionAndEvictLeastRecentlyUsed()
    {
        var cache = new RowPrototypeCache();
        for (var row = 1; row <= RowPrototypeCache.MaximumEntries; row++) cache.Store(row, 1, new XElement("row"));
        cache.Clone(1);
        cache.Store(65, 1, new XElement("row"));
        Assert.NotNull(cache.Clone(1));
        Assert.Null(cache.Clone(2));
        Assert.Equal(RowPrototypeCache.MaximumEntries, cache.Count);

        for (var row = 100; row < 110; row++)
        {
            cache.Store(row, RowPrototypeCache.MaximumRowBytes, new XElement("row"));
            Assert.InRange(cache.SourceBytes, 0, RowPrototypeCache.MaximumBytes);
        }
        Assert.Equal(4, cache.Count);
        var large = new XElement("row", new string('x', RowPrototypeCache.MaximumRowBytes));
        Assert.Same(large, cache.Store(200, RowPrototypeCache.MaximumRowBytes + 1, large));
        Assert.Null(cache.Clone(200));
        Assert.Equal(4, cache.Count);
    }
}
