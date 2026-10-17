using System.Globalization;
using ExcelRenderer.OpenXml.Diagnostics;
using ExcelRenderer.OpenXml.Reading;
using Xunit;

namespace ExcelRenderer.OpenXml.Tests;

public sealed class FormattingAndMetadataTests
{
    [Fact]
    public void ExplicitZeroRowColumnAndParentStylePrecedence()
    {
        using var input = Fixture.Create("<cols><col min=\"1\" max=\"5\" style=\"2\" width=\"9\"/><col min=\"3\" max=\"4\" style=\"0\" width=\"12\"/></cols><sheetData><row s=\"1\" customFormat=\"1\" ht=\"18\" customHeight=\"1\" hidden=\"0\"><c s=\"0\"/><c/><c/></row><row><c/><c/><c/></row></sheetData>", Fixture.Styles);
        using var reader = OpenXmlWorkbookReader.Open(input);
        var all = reader.EnumerateCells(1).ToArray();
        Assert.Equal(new[] { 0, 1, 1, 2, 2, 0 }, all.Select(c => c.StyleIndex));
        var style = reader.ResolveStyle(all[1]);
        Assert.Equal(1, style.FontId);
        Assert.Equal(0, style.FillId);
        Assert.Equal("0", style.Attributes["applyFont"]);
        Assert.Equal("Arial", style.Font.Children.Single(c => c.Name == "name").Attributes["val"]);
        Assert.Equal("Calibri", reader.Workbook.DefaultFont.Children.Single(c => c.Name == "name").Attributes["val"]);
        var meta = reader.ReadWorksheetInfo(1);
        Assert.Equal(new[] { (1, 2), (3, 4), (5, 5) }, meta.Columns.Select(c => (c.Min, c.Max)));
        Assert.Equal(18, meta.Rows[0].HeightPt);
        Assert.True(meta.Rows[0].CustomHeight);
        Assert.False(meta.Rows[0].Hidden);
        Assert.Same(style, reader.ResolveStyle(all[2]));
    }

    [Theory]
    [InlineData("s=\"99\"", "")]
    [InlineData("s=\"1\"", "fontId=\"99\"")]
    [InlineData("s=\"1\"", "fillId=\"99\"")]
    [InlineData("s=\"1\"", "borderId=\"99\"")]
    [InlineData("s=\"1\"", "xfId=\"99\"")]
    public void InvalidStyleReferencesFail(string attribute, string xf)
    {
        var styles = xf == "" ? Fixture.Styles : Fixture.Styles.Replace("<xf fontId=\"1\" numFmtId=\"2\" xfId=\"0\" applyFont=\"0\"/>", $"<xf {xf}/>");
        using var input = Fixture.Create(Fixture.Cells($"<c {attribute}/>"), styles);
        using var reader = OpenXmlWorkbookReader.Open(input);
        Assert.Throws<InvalidDataException>(() => reader.EnumerateCells(1).Single());
    }

    [Theory]
    [InlineData(0, null, 1234.5, false, "en-US", "1234.5")]
    [InlineData(2, null, 1234.5, false, "en-US", "1234.50")]
    [InlineData(9, null, 0.25, false, "en-US", "25%")]
    [InlineData(11, null, 12345, false, "en-US", "1.23E+04")]
    [InlineData(12, null, 1.5, false, "en-US", "1 1/2")]
    [InlineData(164, "$0.00", 12.5, false, "en-US", "$12.50")]
    [InlineData(164, "yyyy-mm-dd", 59, false, "en-US", "1900-02-28")]
    [InlineData(164, "yyyy-mm-dd", 60, false, "en-US", "1900-02-29")]
    [InlineData(164, "yyyy-mm-dd", 61, false, "en-US", "1900-03-01")]
    [InlineData(164, "yyyy-mm-dd", 0, true, "en-US", "1904-01-01")]
    [InlineData(164, "yyyy-mm-dd", 1, true, "en-US", "1904-01-02")]
    [InlineData(164, "h:mm", 0.5, true, "en-US", "12:00")]
    [InlineData(46, null, 1.125, false, "en-US", "27:00:00")]
    [InlineData(46, null, 1.125, true, "en-US", "27:00:00")]
    [InlineData(46, null, 1.1458333333333333, true, "en-US", "27:29:60")]
    [InlineData(2, null, 1.5, false, "ja-JP", "1.50")]
    public void NumberFormatFixedVersionContract(int id, string? code, double number, bool date1904, string culture, string expected)
    {
        var styles = NumberStyles(id, code);
        using var input = Fixture.Create(Fixture.Cells($"<c><v>{number.ToString("R", CultureInfo.InvariantCulture)}</v></c>"), styles, date1904: date1904);
        using var reader = OpenXmlWorkbookReader.Open(input, new OpenXmlReaderOptions { Culture = CultureInfo.GetCultureInfo(culture) });
        var formatted = reader.FormatCell(reader.EnumerateCells(1).Single());
        Assert.Equal(expected, formatted.Text);
        Assert.Empty(formatted.Diagnostics);
    }

    [Theory]
    [InlineData(27, null)]
    [InlineData(164, "[$-411]ggge年m月d日")]
    [InlineData(164, "[DBNum1]0")]
    [InlineData(164, "[invalid")]
    public void UnsupportedFormatsRetainCodeAndReportFallback(int id, string? code)
    {
        using var input = Fixture.Create(Fixture.Cells("<c><v>2.5</v></c>"), NumberStyles(id, code));
        var sink = new List<ReaderDiagnostic>();
        using var reader = OpenXmlWorkbookReader.Open(input, new OpenXmlReaderOptions { Culture = CultureInfo.InvariantCulture, DiagnosticSink = sink.Add });
        var cell = reader.EnumerateCells(1).Single();
        var display = reader.FormatCell(cell);
        Assert.Equal("2.5", display.Text);
        Assert.Equal("UnsupportedNumberFormat", display.Diagnostics.Single().Code);
        Assert.Equal(code, reader.ResolveStyle(cell).NumberFormatCode);
        Assert.Single(sink);
    }

    [Fact]
    public void CachedFormulaPresenceAndGroupsAreIndependentOfValue()
    {
        var xml = "<sheetData><row><c t=\"b\"><f>UNKNOWN()</f><v>0</v></c><c><f>EXTERNAL()</f><v>0</v></c><c t=\"str\"><f>TEXT()</f><v/></c><c><f>HYPERLINK()</f></c><c t=\"e\"><f>ERROR()</f><v>#N/A</v></c><c><f t=\"shared\" si=\"7\"/><v>12</v></c><c><f t=\"shared\" si=\"7\" ref=\"F1:G1\">A1+1</f><v>13</v></c></row><row><c><f t=\"array\" ref=\"A2:B2\">SUM(A1:B1)</f><v>1</v></c><c><v>2</v></c><c/></row></sheetData>";
        using var input = Fixture.Create(xml);
        using var reader = OpenXmlWorkbookReader.Open(input);
        var all = reader.EnumerateCells(1).ToArray();
        Assert.Equal("FALSE", reader.FormatCell(all[0]).Text);
        Assert.Equal("0", reader.FormatCell(all[1]).Text);
        Assert.Empty(reader.FormatCell(all[2]).Diagnostics);
        Assert.Equal("", reader.FormatCell(all[3]).Text);
        Assert.Equal("FormulaCachedValueMissing", reader.FormatCell(all[3]).Diagnostics.Single().Code);
        Assert.Equal("#N/A", reader.FormatCell(all[4]).Text);
        Assert.Equal("FormulaResultError", reader.FormatCell(all[4]).Diagnostics.Single().Code);
        Assert.Equal("", all[5].Formula!.RawFormula);
        Assert.Equal("G1", all[5].Formula!.MasterAddress.ToString());
        Assert.Equal("A1+1", all[5].Formula!.MasterFormula);
        Assert.Equal("array", all[8].Formula!.Kind);
        Assert.Equal("A2", all[8].Formula!.MasterAddress.ToString());
        Assert.Null(all[9].Formula);
        Assert.Empty(reader.FormatCell(all[9]).Diagnostics);
    }

    [Fact]
    public void PrintMetadataAndQuotedUnionRemainInOriginalUnits()
    {
        const string definitions = "<definedName name=\"_xlnm.Print_Area\" localSheetId=\"1\">'O''Brien, Sales'!$A$1:$B$2,'O''Brien, Sales'!D4:E5</definedName><definedName name=\"_xlnm.Print_Titles\" localSheetId=\"1\">'O''Brien, Sales'!$1:$3,'O''Brien, Sales'!$A:$C</definedName>";
        const string body = "<sheetPr><pageSetUpPr fitToPage=\"1\"/></sheetPr><dimension ref=\"A1:XFD1048576\"/><sheetFormatPr defaultRowHeight=\"15\" defaultColWidth=\"0\" zeroHeight=\"0\"/><sheetData><row><c/></row></sheetData><mergeCells><mergeCell ref=\"A1:B2\"/><mergeCell ref=\"A1:B2\"/></mergeCells><printOptions horizontalCentered=\"1\" gridLines=\"0\"/><pageMargins left=\"0.5\" right=\"0.6\" top=\"0.7\" bottom=\"0.8\" header=\"0.2\" footer=\"0.3\"/><pageSetup paperSize=\"9\" scale=\"75\" fitToWidth=\"0\" orientation=\"landscape\"/><headerFooter differentFirst=\"1\"><oddHeader>&amp;P test</oddHeader><firstFooter>one</firstFooter></headerFooter><rowBreaks count=\"99\"><brk id=\"20\" min=\"0\" max=\"16383\" man=\"1\"/></rowBreaks><colBreaks><brk id=\"4\" min=\"0\" max=\"1048575\" man=\"0\"/></colBreaks><drawing/><hyperlinks/><conditionalFormatting/><tableParts/>";
        using var input = Fixture.Create(body, definitions: definitions, chartFirst: true, name: "O'Brien, Sales");
        using var reader = OpenXmlWorkbookReader.Open(input);
        var info = reader.ReadWorksheetInfo(2);
        Assert.Single(info.MergedRanges);
        Assert.Equal("0", info.PageSetup!.Attributes["fitToWidth"]);
        Assert.False(info.PageSetup.Attributes.ContainsKey("fitToHeight"));
        Assert.Equal("75", info.PageSetup.Attributes["scale"]);
        Assert.Equal("1", info.PageSetupProperties!.Attributes["fitToPage"]);
        Assert.Equal("0.5", info.PageMargins!.Attributes["left"]);
        Assert.Equal("&P test", info.HeaderFooter!.Children[0].Text);
        Assert.Equal("0", info.SheetFormat!.Attributes["defaultColWidth"]);
        Assert.Equal(15, info.DefaultRowHeightPt);
        Assert.Equal(0, info.DefaultColumnWidthExcel);
        Assert.Equal(8, info.BaseColumnWidthCharacters);
        Assert.False(info.DefaultRowsHidden);
        Assert.Equal("1", info.RowBreaks.Single().Attributes["man"]);
        Assert.Single(info.ColumnBreaks);
        Assert.True(info.HasDrawings && info.HasHyperlinks && info.HasConditionalFormatting && info.HasTables);
        Assert.Equal(2, info.PrintReferences[0].Ranges.Count);
        Assert.Equal(4, info.PrintReferences[0].Ranges[1].FirstRow);
        Assert.Equal(new[] { "Rows", "Columns" }, info.PrintReferences[1].Ranges.Select(r => r.Kind));
        Assert.Equal("A1:A1", info.ActualCellRange!.ToString());
    }

    [Theory]
    [InlineData("Sheet1!A1:B2,OFFSET(A1,0,0)")]
    [InlineData("[Other.xlsx]Sheet1!A1:B2")]
    [InlineData("Sheet1:Second!A1:B2")]
    [InlineData("OtherName")]
    public void UnsupportedPrintDefinitionRejectsWholeUnion(string reference)
    {
        using var input = Fixture.Create(Fixture.Cells("<c/>"), definitions: $"<definedName name=\"_xlnm.Print_Area\" localSheetId=\"0\">{reference}</definedName>");
        var diagnostics = new List<ReaderDiagnostic>();
        using var reader = OpenXmlWorkbookReader.Open(input, new OpenXmlReaderOptions { DiagnosticSink = diagnostics.Add });
        Assert.Empty(reader.ReadWorksheetInfo(1).PrintReferences.Single().Ranges);
        Assert.Equal("UnsupportedPrintReference", diagnostics.Single().Code);
    }

    [Fact]
    public void PartiallyOverlappingMergesFail()
    {
        using var input = Fixture.Create("<sheetData/><mergeCells><mergeCell ref=\"A1:B2\"/><mergeCell ref=\"B2:C3\"/></mergeCells>");
        using var reader = OpenXmlWorkbookReader.Open(input);
        Assert.Throws<InvalidDataException>(() => reader.ReadWorksheetInfo(1));
    }

    [Fact]
    public void ThemeColorTintAndOriginalFontSchemeArePreserved()
    {
        const string theme = "<a:theme xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" name=\"test\"><a:themeElements><a:clrScheme name=\"test\"><a:dk1><a:srgbClr val=\"000000\"/></a:dk1><a:lt1><a:srgbClr val=\"FFFFFF\"/></a:lt1><a:dk2><a:srgbClr val=\"112233\"/></a:dk2><a:lt2><a:srgbClr val=\"EEEEEE\"/></a:lt2><a:accent1><a:srgbClr val=\"336699\"/></a:accent1></a:clrScheme><a:fontScheme name=\"test\"><a:majorFont><a:latin typeface=\"Cambria\"/><a:ea typeface=\"Yu Mincho\"/><a:font script=\"Jpan\" typeface=\"游明朝\"/></a:majorFont><a:minorFont><a:latin typeface=\"Calibri\"/></a:minorFont></a:fontScheme></a:themeElements></a:theme>";
        var styles = Fixture.Styles.Replace("<name val=\"Calibri\"/>", "<name val=\"Calibri\"/><scheme val=\"minor\"/><color theme=\"4\" tint=\"0.5\"/>");
        using var input = Fixture.Create(Fixture.Cells("<c/>"), styles, theme: theme);
        using var reader = OpenXmlWorkbookReader.Open(input);
        var font = reader.ResolveStyle(reader.EnumerateCells(1).Single()).Font;
        var color = font.Children.Single(c => c.Name == "color");
        Assert.Equal("4", color.Attributes["theme"]);
        Assert.Equal("8CB3D9", color.Attributes["resolvedRgb"]);
        Assert.Equal("minor", font.Children.Single(c => c.Name == "scheme").Attributes["val"]);
        Assert.Equal("游明朝", reader.Workbook.Theme!.Children[0].Children.Single(c => c.Name == "font").Attributes["typeface"]);
    }

    private static string NumberStyles(int id, string? code)
    {
        var format = code == null ? "" : $"<numFmts count=\"1\"><numFmt numFmtId=\"{id}\" formatCode=\"{System.Security.SecurityElement.Escape(code)}\"/></numFmts>";
        return $"<styleSheet xmlns=\"{Fixture.Ns}\">{format}<cellXfs count=\"1\"><xf numFmtId=\"{id}\"/></cellXfs></styleSheet>";
    }
}
