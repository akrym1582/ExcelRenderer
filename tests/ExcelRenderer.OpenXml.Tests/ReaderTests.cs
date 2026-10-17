using System.Globalization;
using System.Xml;
using ExcelRenderer.OpenXml.Reading;
using Xunit;

namespace ExcelRenderer.OpenXml.Tests;

public sealed class ReaderTests
{
    [Fact]
    public void OwnershipEnumerationAndSnapshots()
    {
        using var input = Fixture.Create(Fixture.Cells("<c r=\"A1\"><v>0</v></c><c r=\"B1\" s=\"0\"/><c r=\"C1\" t=\"str\"><v/></c>"));
        var reader = OpenXmlWorkbookReader.Open(input);
        Assert.Equal(42u, reader.Sheets[0].SheetId);
        Assert.Equal("hidden", reader.Sheets[0].Visibility);
        using (var cursor = reader.EnumerateCells(1).GetEnumerator())
        {
            Assert.True(cursor.MoveNext());
            Assert.Equal("0", reader.FormatCell(cursor.Current).Text);
            Assert.Throws<InvalidOperationException>(() => reader.ReadWorksheetInfo(1));
            Assert.Throws<InvalidOperationException>(() => reader.EnumerateCells(1).First());
        }

        Assert.True(input.CanRead);
        var all = reader.EnumerateCells(1).ToArray();
        Assert.Equal(3, all.Length);
        Assert.Null(all[0].ExplicitStyleIndex);
        Assert.Equal(0, all[1].ExplicitStyleIndex);
        Assert.Equal("Blank", all[1].Value.Kind);
        Assert.Equal("String", all[2].Value.Kind);
        reader.Dispose();
        Assert.True(input.CanRead);
        Assert.Equal(0, all[0].Value.Number);
        Assert.Throws<ObjectDisposedException>(() => reader.Sheets);
        Assert.Throws<ObjectDisposedException>(() => reader.Workbook);
        Assert.Throws<ObjectDisposedException>(() => reader.EnumerateCells(1));
        Assert.Throws<ObjectDisposedException>(() => reader.ReadWorksheetInfo(1));
        Assert.Throws<ObjectDisposedException>(() => reader.ResolveStyle(all[0]));
        Assert.Throws<ObjectDisposedException>(() => reader.FormatCell(all[0]));
    }

    [Fact]
    public void FailedOpenLeavesStreamOpen()
    {
        using var stream = new MemoryStream(new byte[30]);
        Assert.ThrowsAny<Exception>(() => OpenXmlWorkbookReader.Open(stream));
        Assert.True(stream.CanRead);
        stream.Position = 1;
        Assert.Throws<ArgumentException>(() => OpenXmlWorkbookReader.Open(stream));
        using var nonSeekable = new NonSeekableStream();
        Assert.Throws<NotSupportedException>(() => OpenXmlWorkbookReader.Open(nonSeekable));
    }

    [Fact]
    public void DisposeDuringEnumerationAndCancellationReleaseResources()
    {
        using var input = Fixture.Create(Fixture.Cells("<c/><c/>"));
        var reader = OpenXmlWorkbookReader.Open(input);
        using var cursor = reader.EnumerateCells(1).GetEnumerator();
        Assert.True(cursor.MoveNext());
        reader.Dispose();
        Assert.Throws<ObjectDisposedException>(() => cursor.MoveNext());
        Assert.True(input.CanRead);
        input.Position = 0;
        using var second = OpenXmlWorkbookReader.Open(input);
        using var cts = new CancellationTokenSource();
        using (var canceled = second.EnumerateCells(1, cts.Token).GetEnumerator())
        {
            Assert.True(canceled.MoveNext());
            cts.Cancel();
            Assert.Throws<OperationCanceledException>(() => canceled.MoveNext());
        }

        Assert.Equal(2, second.EnumerateCells(1).Count());
        Assert.Throws<OperationCanceledException>(() => second.ReadWorksheetInfo(1, cts.Token));
        input.Position = 0;
        Assert.Throws<OperationCanceledException>(() => OpenXmlWorkbookReader.Open(input, cancellationToken: cts.Token));
    }

    [Theory]
    [InlineData("", "-1.25e2", "Number", "-125")]
    [InlineData("n", "0", "Number", "0")]
    [InlineData("b", "0", "Boolean", "FALSE")]
    [InlineData("b", "1", "Boolean", "TRUE")]
    [InlineData("e", "#DIV/0!", "Error", "#DIV/0!")]
    [InlineData("str", "", "String", "")]
    [InlineData("d", "2026-10-15T12:34:56+09:00", "IsoDate", "2026-10-15T12:34:56+09:00")]
    public void TypedValues(string type, string raw, string kind, string display)
    {
        using var input = Fixture.Create(Fixture.Cells($"<c t=\"{(type == "" ? "n" : type)}\"><v>{raw}</v></c>"));
        using var reader = OpenXmlWorkbookReader.Open(input, new OpenXmlReaderOptions { Culture = CultureInfo.InvariantCulture });
        var cell = reader.EnumerateCells(1).Single();
        Assert.Equal(kind, cell.Value.Kind);
        Assert.Equal(raw, cell.RawValue);
        Assert.Equal(display, reader.FormatCell(cell).Text);
    }

    [Theory]
    [InlineData("n", "NaN")]
    [InlineData("n", "Infinity")]
    [InlineData("n", "1e999")]
    [InlineData("n", "abc")]
    [InlineData("b", "2")]
    [InlineData("s", "-1")]
    [InlineData("s", "2147483648")]
    [InlineData("s", "0")]
    [InlineData("d", "garbage")]
    public void BadValuesFail(string type, string value)
    {
        using var input = Fixture.Create(Fixture.Cells($"<c t=\"{type}\"><v>{value}</v></c>"));
        using var reader = OpenXmlWorkbookReader.Open(input);
        Assert.Throws<InvalidDataException>(() => reader.EnumerateCells(1).ToArray());
        Assert.True(input.CanRead);
    }

    [Fact]
    public void StringsPreserveWhitespaceRunsPhoneticsAndDecodeOnce()
    {
        const string text = "<t xml:space=\"preserve\">  日本𠮷\U000E0100&amp;</t><r><rPr><b val=\"0\"/></rPr><t>_x0041__x005F_x0042_</t></r><rPh sb=\"0\" eb=\"2\"><t>ニホン</t></rPh>";
        using var input = Fixture.Create(Fixture.Cells($"<c t=\"s\"><v>0</v></c><c t=\"inlineStr\"><is>{text}</is></c><c t=\"s\" s=\"1\"><v>0</v></c>"), Fixture.Styles, "<si>" + text + "</si>");
        using var reader = OpenXmlWorkbookReader.Open(input);
        var cells = reader.EnumerateCells(1).ToArray();
        Assert.Equal("  日本𠮷\U000E0100&A_x0042_", cells[0].Value.Text);
        Assert.Equal(cells[0].Value.Text, cells[1].Value.Text);
        Assert.True(cells[0].HasPhonetic);
        Assert.Equal(2, cells[0].RichTextRuns.Count);
        Assert.Null(cells[0].RichTextRuns[0].Properties);
        Assert.Equal("0", cells[0].RichTextRuns[1].Properties!.Children.Single().Attributes["val"]);
        Assert.Same(cells[0].RichTextRuns, cells[2].RichTextRuns);
        Assert.NotEqual(reader.ResolveStyle(cells[0]).FontId, reader.ResolveStyle(cells[2]).FontId);
    }

    [Theory]
    [InlineData("<row r=\"1\"><c r=\"A2\"/></row>")]
    [InlineData("<row r=\"1\"><c r=\"B1\"/><c r=\"A1\"/></row>")]
    [InlineData("<row r=\"1\"><c r=\"A1\"/><c r=\"A1\"/></row>")]
    [InlineData("<row r=\"2\"/><row r=\"1\"/>")]
    [InlineData("<row r=\"0\"/>")]
    [InlineData("<row r=\"1048577\"/>")]
    [InlineData("<row><c r=\"XFE1\"/></row>")]
    [InlineData("<row><c r=\"$A$1\"/></row>")]
    public void InvalidCoordinatesFail(string rows)
    {
        using var input = Fixture.Create("<sheetData>" + rows + "</sheetData>");
        using var reader = OpenXmlWorkbookReader.Open(input);
        Assert.Throws<InvalidDataException>(() => reader.ReadWorksheetInfo(1));
    }

    [Fact]
    public void MissingCoordinatesAndSparseDimension()
    {
        using var input = Fixture.Create("<dimension ref=\"A1:XFD1048576\"/><sheetData><row><c/><c/></row><row r=\"1048576\"><c r=\"XFD1048576\"/></row></sheetData>");
        using var reader = OpenXmlWorkbookReader.Open(input);
        Assert.Equal(new[] { "A1", "B1", "XFD1048576" }, reader.EnumerateCells(1).Select(c => c.Address.ToString()));
        Assert.Equal("A1:XFD1048576", reader.ReadWorksheetInfo(1).ActualCellRange!.ToString());
        Assert.Equal(3, reader.EnumerateCells(1).Count());
    }

    [Fact]
    public void SelectionPreservesOriginalIndexesAndDefersBodies()
    {
        using var input = Fixture.Create(Fixture.Cells("<c/>"), secondBody: "<sheetData><row><c r=\"broken\"/></row></sheetData>", chartFirst: true);
        using var reader = OpenXmlWorkbookReader.Open(input, new OpenXmlReaderOptions { SelectedSheetNames = new[] { "Sheet1", "Sheet1" } });
        Assert.Equal(new[] { "Chartsheet", "Worksheet", "Worksheet" }, reader.Sheets.Select(s => s.Kind));
        Assert.Equal(2, reader.EnumerateCells(2).Single().SheetIndex);
        Assert.Throws<NotSupportedException>(() => reader.EnumerateCells(1));
        Assert.Throws<InvalidOperationException>(() => reader.EnumerateCells(3));
        Assert.Throws<InvalidDataException>(() => reader.ReadWorksheetInfo(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadWorksheetInfo(0));
        input.Position = 0;
        using var empty = OpenXmlWorkbookReader.Open(input, new OpenXmlReaderOptions { SelectedSheetNames = Array.Empty<string>() });
        Assert.Throws<InvalidOperationException>(() => empty.EnumerateCells(2));
        Assert.NotNull(empty.ReadWorksheetInfo(2));
        input.Position = 0;
        Assert.Throws<ArgumentException>(() => OpenXmlWorkbookReader.Open(input, new OpenXmlReaderOptions { SelectedSheetNames = new[] { "sheet1" } }));
    }

    [Theory]
    [InlineData("items")]
    [InlineData("characters")]
    [InlineData("cell")]
    [InlineData("styles")]
    public void IndependentTableAndTextLimits(string kind)
    {
        var options = new OpenXmlReaderOptions();
        if (kind == "items") options.MaxSharedStringItems = 1;
        if (kind == "characters") options.MaxSharedStringCharacters = 3;
        if (kind == "cell") options.MaxCellTextCharacters = 2;
        if (kind == "styles") options.MaxStyleRecords = 1;
        using var input = Fixture.Create(Fixture.Cells("<c t=\"s\"><v>0</v></c>"), Fixture.Styles, "<si><t>abc</t><rPh sb=\"0\" eb=\"1\"><t>x</t></rPh></si><si><t>z</t></si>");
        Assert.Throws<InvalidDataException>(() =>
        {
            using var reader = OpenXmlWorkbookReader.Open(input, options);
            reader.EnumerateCells(1).ToArray();
        });
        Assert.True(input.CanRead);
    }

    [Fact]
    public void PartLimitIsAppliedBySdk()
    {
        using var input = Fixture.Create(Fixture.Cells("<c t=\"inlineStr\"><is><t>" + new string('x', 4000) + "</t></is></c>"));
        Assert.Throws<InvalidDataException>(() =>
        {
            using var reader = OpenXmlWorkbookReader.Open(input, new OpenXmlReaderOptions { MaxCharactersInPart = 1000 });
            reader.EnumerateCells(1).ToArray();
        });
        Assert.True(input.CanRead);
    }

    [Fact]
    public void OptionsAreValidatedAndCaptured()
    {
        using var input = Fixture.Create(Fixture.Cells("<c><v>1.5</v></c>"));
        Assert.Throws<ArgumentOutOfRangeException>(() => OpenXmlWorkbookReader.Open(input, new OpenXmlReaderOptions { MaxStyleRecords = 0 }));
        input.Position = 0;
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("en-US").Clone();
        var options = new OpenXmlReaderOptions { Culture = culture };
        using var reader = OpenXmlWorkbookReader.Open(input, options);
        culture.NumberFormat.NumberDecimalSeparator = "!";
        options.MaxCellTextCharacters = 1;
        Assert.Equal("1.5", reader.FormatCell(reader.EnumerateCells(1).Single()).Text);
    }

    private sealed class NonSeekableStream : MemoryStream
    {
        public override bool CanSeek => false;
    }
}
