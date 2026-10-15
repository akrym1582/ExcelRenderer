using System.Globalization;
using Xunit;

namespace ExcelRenderer.OpenXml.Tests;

public sealed class AdapterContractTests
{
    [Fact]
    public void InterfaceSupportsSelectionMetadataAndStyleIdsWithoutProductTypes()
    {
        using var input = Fixture.Create("<cols><col min=\"1\" max=\"2\" style=\"2\"/></cols><sheetData><row s=\"1\" customFormat=\"1\"><c><v>1.5</v></c></row></sheetData>", Fixture.Styles, secondBody: Fixture.Cells("<c/>"), chartFirst: true);
        IWorkbookDataReader reader = OpenXmlWorkbookReader.Open(input, new OpenXmlReaderOptions { SelectedSheetNames = new[] { "Second", "Sheet1" }, Culture = CultureInfo.InvariantCulture });
        var selection = reader.SelectedWorksheetIndices;
        Assert.Equal(new[] { 2, 3 }, selection);
        Assert.Equal("Chartsheet", reader.Sheets[0].Kind);
        var info = reader.ReadWorksheetInfo(2);
        Assert.Equal(1, reader.ResolveStyle(info.Rows[0].StyleIndex!.Value).StyleIndex);
        Assert.Equal(2, reader.ResolveStyle(info.Columns[0].StyleIndex!.Value).StyleIndex);
        Assert.Equal(0, reader.ResolveStyle(0).StyleIndex);
        var cell = reader.EnumerateCells(2).Single();
        var style = reader.ResolveStyle(cell.StyleIndex);
        Assert.Same(style, reader.ResolveStyle(cell));
        Assert.Equal(CellValueType.Number, cell.Value.Type);
        Assert.Equal("1.50", reader.FormatCell(cell).Text);
        Assert.Throws<InvalidDataException>(() => reader.ResolveStyle(-1));
        Assert.Throws<InvalidDataException>(() => reader.ResolveStyle(3));
        reader.Dispose();
        Assert.Equal(new[] { 2, 3 }, selection);
        Assert.Equal("Arial", style.FontProperties.Name);
        Assert.Throws<ObjectDisposedException>(() => reader.SelectedWorksheetIndices);
        Assert.Throws<ObjectDisposedException>(() => reader.ResolveStyle(0));
        Assert.True(input.CanRead);
    }

    [Fact]
    public void SelectedWorksheetPositionsExcludeChartsAndRespectEmptySelection()
    {
        using var input = Fixture.Create(Fixture.Cells("<c/>"), chartFirst: true);
        using (var all = OpenXmlWorkbookReader.Open(input))
        {
            Assert.Equal(new[] { 2 }, all.SelectedWorksheetIndices);
        }

        input.Position = 0;
        using (var chart = OpenXmlWorkbookReader.Open(input, new OpenXmlReaderOptions { SelectedSheetNames = new[] { "Chart" } }))
        {
            Assert.Empty(chart.SelectedWorksheetIndices);
        }

        input.Position = 0;
        using var empty = OpenXmlWorkbookReader.Open(input, new OpenXmlReaderOptions { SelectedSheetNames = Array.Empty<string>() });
        Assert.Empty(empty.SelectedWorksheetIndices);
    }

    [Theory]
    [InlineData("<c/>", CellValueType.Blank, "Blank")]
    [InlineData("<c><v>0</v></c>", CellValueType.Number, "Number")]
    [InlineData("<c t=\"str\"><v/></c>", CellValueType.String, "String")]
    [InlineData("<c t=\"b\"><v>0</v></c>", CellValueType.Boolean, "Boolean")]
    [InlineData("<c t=\"e\"><v>#N/A</v></c>", CellValueType.Error, "Error")]
    [InlineData("<c t=\"d\"><v>2026-10-15</v></c>", CellValueType.IsoDate, "IsoDate")]
    public void TypedDispatchPreservesExistingKind(string xml, CellValueType type, string kind)
    {
        using var input = Fixture.Create(Fixture.Cells(xml));
        using var reader = OpenXmlWorkbookReader.Open(input);
        var value = reader.EnumerateCells(1).Single().Value;
        Assert.Equal(type, value.Type);
        Assert.Equal(kind, value.Kind);
    }

    [Fact]
    public void PartialFontOverlaysAreIndependentForEachCellAndSurviveDisposal()
    {
        var styles = Fixture.Styles.Replace("<name val=\"Calibri\"/>", "<name val=\"Calibri\"/><color rgb=\"FF112233\"/><scheme val=\"minor\"/>");
        using var input = Fixture.Create(Fixture.Cells("<c t=\"s\"><v>0</v></c><c t=\"s\" s=\"1\"><v>0</v></c>"), styles,
            "<si><r><rPr><b val=\"0\"/><u val=\"none\"/><i/><sz val=\"14\"/></rPr><t>partial</t></r><r><rPr><rFont val=\"Custom\"/><color indexed=\"10\"/><vertAlign val=\"superscript\"/><strike/></rPr><t>named</t></r><t>plain</t></si>");
        var reader = OpenXmlWorkbookReader.Open(input);
        var cells = reader.EnumerateCells(1).ToArray();
        var firstBase = reader.ResolveStyle(cells[0]).FontProperties;
        var secondBase = reader.ResolveStyle(cells[1]).FontProperties;
        Assert.True(secondBase.Bold);
        Assert.Null(cells[0].RichTextRuns[0].FontProperties!.Name);
        Assert.False(cells[0].RichTextRuns[0].FontProperties!.Bold);
        var first = firstBase.WithOverrides(cells[0].RichTextRuns[0].FontProperties);
        var second = secondBase.WithOverrides(cells[1].RichTextRuns[0].FontProperties);
        Assert.Equal("Calibri", first.Name);
        Assert.Equal("Arial", second.Name);
        Assert.False(first.Bold);
        Assert.False(second.Bold);
        Assert.True(first.Italic);
        Assert.Equal("none", first.Underline);
        Assert.Equal(14, first.SizePt);
        Assert.Equal("minor", first.Scheme);
        Assert.Equal("112233", first.Color!.Attributes["resolvedRgb"]);
        Assert.Same(firstBase, firstBase.WithOverrides(cells[0].RichTextRuns[2].FontProperties));
        var named = firstBase.WithOverrides(cells[0].RichTextRuns[1].FontProperties);
        Assert.Equal("Custom", named.Name);
        Assert.Equal("FF0000", named.Color!.Attributes["resolvedRgb"]);
        Assert.Equal("superscript", named.VerticalAlignment);
        Assert.True(named.Strikethrough);
        Assert.Same(cells[0].RichTextRuns, cells[1].RichTextRuns);
        Assert.Equal("Calibri", reader.Workbook.DefaultFontProperties.Name);
        reader.Dispose();
        Assert.Equal("Arial", second.Name);
        Assert.True(secondBase.Bold);
        Assert.Null(cells[0].RichTextRuns[0].FontProperties!.Name);
    }

    [Fact]
    public void AbsentFontPropertiesRemainUnspecified()
    {
        using var input = Fixture.Create(Fixture.Cells("<c/>"));
        using IWorkbookDataReader reader = OpenXmlWorkbookReader.Open(input);
        var font = reader.ResolveStyle(0).FontProperties;
        Assert.Null(font.Name);
        Assert.Null(font.SizePt);
        Assert.Null(font.Bold);
        Assert.Null(font.Color);
        Assert.Same(font, font.WithOverrides(null));
    }

    [Theory]
    [InlineData("A1\n")]
    [InlineData("A01")]
    [InlineData("A2147483648")]
    [InlineData("A999999999999999999999")]
    [InlineData("ZZZZ1")]
    [InlineData("$A$1")]
    [InlineData("a1")]
    public void FastCoordinateParserRejectsMalformedInputs(string text) => Assert.Throws<InvalidDataException>(() => CellAddress.Parse(text));

    [Theory]
    [InlineData("$A$1")]
    [InlineData("A$1")]
    [InlineData("$A1")]
    [InlineData("A1")]
    public void AbsoluteRangeParsingPreservesCoordinateContract(string text) => Assert.Equal("A1:A1", CellRange.Parse(text).ToString());

    [Fact]
    public void SdkLeafTraversalPreservesAdjacentEmptyCellsFormulasAndInlinePhonetics()
    {
        using var input = Fixture.Create(Fixture.Cells("<c/><c t=\"inlineStr\"><f>TEXT()</f><is><r><t>_x005F_x0041_</t></r><rPh sb=\"0\" eb=\"1\"><t>reading</t></rPh></is></c><c><f/><v>0</v></c><c t=\"str\"><v/></c><c/>"));
        using var reader = OpenXmlWorkbookReader.Open(input);
        var cells = reader.EnumerateCells(1).ToArray();
        Assert.Equal(5, cells.Length);
        Assert.Equal("_x0041_", cells[1].Value.Text);
        Assert.True(cells[1].HasPhonetic);
        Assert.True(cells[1].Formula!.HasCachedResult);
        Assert.Equal(0, cells[2].Value.Number);
        Assert.Equal(string.Empty, cells[3].RawValue);
        Assert.Null(cells[4].RawValue);
        Assert.Equal(5, reader.EnumerateCells(1).Count());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StreamingTextLimitsIncludePhoneticsInBothPasses(bool shared)
    {
        var text = "<t>a</t><rPh sb=\"0\" eb=\"1\"><t>reading</t></rPh>";
        using var input = Fixture.Create(Fixture.Cells(shared ? "<c t=\"s\"><v>0</v></c>" : "<c t=\"inlineStr\"><is>" + text + "</is></c>"), sst: shared ? "<si>" + text + "</si>" : null);
        using var reader = OpenXmlWorkbookReader.Open(input, new OpenXmlReaderOptions { MaxCellTextCharacters = 7 });
        Assert.Throws<InvalidDataException>(() => reader.EnumerateCells(1).ToArray());
    }
}
