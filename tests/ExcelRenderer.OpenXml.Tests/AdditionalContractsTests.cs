using System.Globalization;
using ExcelRenderer.OpenXml.Diagnostics;
using ExcelRenderer.OpenXml.Model.Cells;
using ExcelRenderer.OpenXml.Reading;
using Xunit;

namespace ExcelRenderer.OpenXml.Tests;

public sealed class AdditionalContractsTests
{
    [Theory]
    [InlineData("A1", 1, 1)]
    [InlineData("XFD1048576", 1048576, 16384)]
    public void CoordinateExtremes(string reference, int row, int column)
    {
        var address = CellAddress.Parse(reference);
        Assert.Equal(row, address.Row);
        Assert.Equal(column, address.Column);
        Assert.Equal(reference, address.ToString());
        Assert.Equal(address, new CellAddress(row, column));
        Assert.Equal(address.GetHashCode(), new CellAddress(row, column).GetHashCode());
    }

    [Theory]
    [InlineData("A0")]
    [InlineData("XFE1")]
    [InlineData("A1048577")]
    [InlineData("$A$1")]
    [InlineData("A1:B2")]
    public void AddressParserRejectsNonCoordinates(string reference) => Assert.Throws<InvalidDataException>(() => CellAddress.Parse(reference));

    [Fact]
    public void RangeParserDistinguishesAbsoluteReferencesAndRejectsReversal()
    {
        Assert.Equal("A1:B2", CellRange.Parse("$A$1:$B$2").ToString());
        Assert.Equal("A1:A1", CellRange.Parse("A1").ToString());
        Assert.Throws<InvalidDataException>(() => CellRange.Parse("B2:A1"));
        Assert.Throws<InvalidDataException>(() => new CellRange(default, default));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CellAddress(0, 1));
    }

    [Theory]
    [InlineData("false")]
    [InlineData("true")]
    [InlineData(null)]
    public void ApplyFlagsNeverDiscardSavedComponents(string? apply)
    {
        var attributes = apply == null ? "" : $" applyFont=\"{apply}\" applyNumberFormat=\"{apply}\"";
        var styles = Fixture.Styles.Replace(" applyFont=\"0\"", attributes);
        using var input = Fixture.Create(Fixture.Cells("<c s=\"1\"><v>1</v></c>"), styles);
        using var reader = OpenXmlWorkbookReader.Open(input, new OpenXmlReaderOptions { Culture = CultureInfo.InvariantCulture });
        var cell = reader.EnumerateCells(1).Single();
        Assert.Equal(1, reader.ResolveStyle(cell).FontId);
        Assert.Equal("1.00", reader.FormatCell(cell).Text);
    }

    [Fact]
    public void XfParentSuppliesMissingFontAlignmentAndFormat()
    {
        var styles = Fixture.Styles.Replace("<xf fontId=\"0\" fillId=\"0\" borderId=\"0\" numFmtId=\"0\"/>", "<xf fontId=\"1\" fillId=\"0\" borderId=\"0\" numFmtId=\"9\"><alignment textRotation=\"255\" relativeIndent=\"2\"/></xf>");
        styles = styles.Replace("<xf fontId=\"1\" numFmtId=\"2\" xfId=\"0\" applyFont=\"0\"/>", "<xf xfId=\"0\" quotePrefix=\"1\"/>");
        using var input = Fixture.Create(Fixture.Cells("<c s=\"1\"><v>0.5</v></c>"), styles);
        using var reader = OpenXmlWorkbookReader.Open(input);
        var cell = reader.EnumerateCells(1).Single();
        var style = reader.ResolveStyle(cell);
        Assert.Equal(1, style.FontId);
        Assert.Equal("255", style.Alignment!.Attributes["textRotation"]);
        Assert.Equal("2", style.Alignment.Attributes["relativeIndent"]);
        Assert.Equal("1", style.Attributes["quotePrefix"]);
        Assert.Equal("50%", reader.FormatCell(cell).Text);
    }

    [Theory]
    [InlineData("rgb=\"FFFF0000\"", "FF0000")]
    [InlineData("indexed=\"10\"", "FF0000")]
    [InlineData("rgb=\"FF336699\" tint=\"-0.5\"", "1A334D")]
    public void ColorSourcesAndHlsTint(string attributes, string rgb)
    {
        var styles = Fixture.Styles.Replace("<sz val=\"11\"/>", $"<sz val=\"11\"/><color {attributes}/>");
        using var input = Fixture.Create(Fixture.Cells("<c/>"), styles);
        using var reader = OpenXmlWorkbookReader.Open(input);
        var color = reader.ResolveStyle(reader.EnumerateCells(1).Single()).Font.Children.Single(c => c.Name == "color");
        Assert.Equal(rgb, color.Attributes["resolvedRgb"]);
        Assert.True(color.Attributes.ContainsKey(attributes.StartsWith("indexed", StringComparison.Ordinal) ? "indexed" : "rgb"));
    }

    [Fact]
    public void UnknownColorRetainsSourceAndReportsWithoutValues()
    {
        var styles = Fixture.Styles.Replace("<sz val=\"11\"/>", "<sz val=\"11\"/><color indexed=\"99\"/>");
        using var input = Fixture.Create(Fixture.Cells("<c/>"), styles);
        var diagnostics = new List<ReaderDiagnostic>();
        using var reader = OpenXmlWorkbookReader.Open(input, new OpenXmlReaderOptions { DiagnosticSink = diagnostics.Add });
        var color = reader.ResolveStyle(reader.EnumerateCells(1).Single()).Font.Children.Single(c => c.Name == "color");
        Assert.False(color.Attributes.ContainsKey("resolvedRgb"));
        Assert.Equal("99", color.Attributes["indexed"]);
        Assert.All(diagnostics, d => Assert.Equal(ReaderDiagnosticCodes.UnresolvedColor, d.Code));
        Assert.All(diagnostics, d => Assert.DoesNotContain("99", d.Message));
        Assert.NotEmpty(diagnostics);
    }

    [Theory]
    [InlineData("part")]
    [InlineData("items")]
    [InlineData("characters")]
    [InlineData("styles")]
    [InlineData("cell")]
    public void EveryLimitMustBePositive(string limit)
    {
        var options = new OpenXmlReaderOptions();
        switch (limit)
        {
            case "part": options.MaxCharactersInPart = 0; break;
            case "items": options.MaxSharedStringItems = -1; break;
            case "characters": options.MaxSharedStringCharacters = 0; break;
            case "styles": options.MaxStyleRecords = 0; break;
            case "cell": options.MaxCellTextCharacters = 0; break;
        }

        using var input = Fixture.Create("<sheetData/>");
        Assert.Throws<ArgumentOutOfRangeException>(() => OpenXmlWorkbookReader.Open(input, options));
        Assert.True(input.CanRead);
    }

    [Fact]
    public void SharedTableIsLazyAndRejectsOutOfBoundsIndex()
    {
        using var input = Fixture.Create(Fixture.Cells("<c t=\"s\"><v>1</v></c>"), sst: "<si><t>only</t></si>");
        using var reader = OpenXmlWorkbookReader.Open(input);
        Assert.Equal(0, reader.LoadedSharedStringCount);
        reader.ReadWorksheetInfo(1);
        Assert.Equal(0, reader.LoadedSharedStringCount);
        Assert.Throws<InvalidDataException>(() => reader.EnumerateCells(1).Single());
        Assert.Equal(1, reader.LoadedSharedStringCount);
    }

    [Fact]
    public void UnselectedMalformedXmlIsNotOpenedEvenWhenSstExists()
    {
        using var input = Fixture.Create(Fixture.Cells("<c><v>1</v></c>"), sst: "<si><t>unused</t></si>", secondBody: "<sheetData><row>");
        using var reader = OpenXmlWorkbookReader.Open(input, new OpenXmlReaderOptions { SelectedSheetNames = new[] { "Sheet1" } });
        Assert.Single(reader.EnumerateCells(1));
        Assert.Equal(0, reader.LoadedSharedStringCount);
    }

    [Fact]
    public void CustomIndexedPaletteIsReadEntryByEntry()
    {
        var styles = Fixture.Styles.Replace("<sz val=\"11\"/>", "<sz val=\"11\"/><color indexed=\"0\"/>");
        styles = styles.Replace("</styleSheet>", "<colors><indexedColors><rgbColor rgb=\"FF123456\"/></indexedColors></colors></styleSheet>");
        using var input = Fixture.Create(Fixture.Cells("<c/>"), styles);
        using var reader = OpenXmlWorkbookReader.Open(input);
        var color = reader.ResolveStyle(reader.EnumerateCells(1).Single()).Font.Children.Single(c => c.Name == "color");
        Assert.Equal("123456", color.Attributes["resolvedRgb"]);
    }

    [Fact]
    public void TextSectionFormattingUsesStoredStringWithoutCalculation()
    {
        var styles = Fixture.Styles.Replace("<fonts", "<numFmts><numFmt numFmtId=\"164\" formatCode=\"0;0;0;&amp;quot;prefix &amp;quot;@\"/></numFmts><fonts");
        styles = styles.Replace("&amp;quot;", "&quot;").Replace("numFmtId=\"2\"", "numFmtId=\"164\"");
        using var input = Fixture.Create(Fixture.Cells("<c t=\"str\" s=\"1\"><f>UNKNOWN()</f><v>abc</v></c>"), styles);
        using var reader = OpenXmlWorkbookReader.Open(input);
        Assert.Equal("prefix abc", reader.FormatCell(reader.EnumerateCells(1).Single()).Text);
    }

    [Fact]
    public void CommentsInsideCellsAndRichStringsDoNotBecomeText()
    {
        using var input = Fixture.Create(Fixture.Cells("<c t=\"inlineStr\"><!--cell--><is><!--string--><r><!--run--><t>text</t></r></is></c>"));
        using var reader = OpenXmlWorkbookReader.Open(input);
        Assert.Equal("text", reader.EnumerateCells(1).Single().Value.Text);
    }

    [Fact]
    public void TinyWorkbookPartLimitIsAnInvalidDataFailureAndLeavesInputOpen()
    {
        using var input = Fixture.Create("<sheetData/>");
        Assert.Throws<InvalidDataException>(() => OpenXmlWorkbookReader.Open(input, new OpenXmlReaderOptions { MaxCharactersInPart = 1 }));
        Assert.True(input.CanRead);
    }
}
