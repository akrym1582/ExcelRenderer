using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.CoreIntegration;
using ExcelRenderer.Excel;
using ExcelRenderer.Formulas.Values;
using ExcelRenderer.Model;
using ExcelRenderer.OpenXml.Calculation;
using ExcelRenderer.OpenXml.Reader;
using ExcelRenderer.Rendering;
using Xunit;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.Tests;

public sealed partial class ConditionalFormattingTests
{
    [Theory]
    [InlineData("top10", "rank=\"2\"", "100,90,90,80", "1110")]
    [InlineData("top10", "rank=\"50\" percent=\"1\"", "0,1,2", "011")]
    [InlineData("top10", "rank=\"100\" percent=\"1\"", "-3,0,2", "111")]
    [InlineData("top10", "rank=\"9\"", "-3,0,2", "111")]
    [InlineData("top10", "rank=\"1\" bottom=\"true\"", "-3,-3,0,2", "1100")]
    [InlineData("top10", "", "5", "1")]
    [InlineData("aboveAverage", "", "0,10,20", "001")]
    [InlineData("aboveAverage", "equalAverage=\"1\"", "0,10,20", "011")]
    [InlineData("aboveAverage", "aboveAverage=\"0\"", "0,10,20", "100")]
    [InlineData("aboveAverage", "aboveAverage=\"0\" equalAverage=\"1\"", "0,10,20", "110")]
    [InlineData("aboveAverage", "stdDev=\"1\"", "0,10,20", "001")]
    [InlineData("aboveAverage", "stdDev=\"2\"", "0,10,20", "000")]
    [InlineData("aboveAverage", "stdDev=\"3\" aboveAverage=\"0\"", "0,10,20", "000")]
    [InlineData("aboveAverage", "", "7,7,7", "000")]
    [InlineData("aboveAverage", "equalAverage=\"true\" stdDev=\"1\"", "7,7,7", "111")]
    [InlineData("aboveAverage", "", "7", "0")]
    [InlineData("aboveAverage", "", "1000000000000000,1000000000000001,1000000000000002", "001")]
    [InlineData("aboveAverage", "aboveAverage=\"0\"", "-20,-10,0", "100")]
    [InlineData("duplicateValues", "", "1,2,1,1,3", "10110")]
    [InlineData("uniqueValues", "", "1,2,1,1,3", "01001")]
    public void PopulationRulesHaveIndependentNumericExpectations(string type, string attributes, string numbers, string expected)
    {
        using var input = Book(NumericRows(numbers), Rule(type, "", ranges: "A1:A" + expected.Length, extra: attributes));
        var sheet = Read(input);
        Assert.Empty(sheet.CellReadDiagnostics);
        Assert.Equal(expected, Painted(sheet, expected.Length));
    }

    [Theory]
    [InlineData("top10", "rank=\"0\"")]
    [InlineData("top10", "rank=\"1001\"")]
    [InlineData("top10", "rank=\"101\" percent=\"1\"")]
    [InlineData("top10", "rank=\"2.5\"")]
    [InlineData("top10", "bottom=\"maybe\"")]
    [InlineData("aboveAverage", "stdDev=\"4\"")]
    [InlineData("aboveAverage", "equalAverage=\"maybe\"")]
    public void InvalidPopulationAttributesProtectLowerPriorityFill(string type, string attributes)
    {
        using var input = Book(NumericRows("1,2"), Rule(type, "", ranges: "A1:A2", extra: attributes) + Rule("expression", "<formula>TRUE</formula>", priority: 2, ranges: "A1:A2"));
        var sheet = Read(input);
        Assert.Equal("00", Painted(sheet, 2));
        Assert.Contains(SourceFeatures(input), feature => feature.Code == "ConditionalFormattingAttributesInvalid");
    }

    [Fact]
    public void DuplicateKeysKeepTypesDatesBlanksAndOrdinalCaseIndependentOfCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            var cells = new[]
            {
                "<v>1</v>", "<v>1</v>", "<is><t>1</t></is>", "<v>1</v>",
                "<is><t>FILE</t></is>", "<is><t>file</t></is>", "<is><t>fıle</t></is>",
                "", "", "<f>\"\"</f><v/>", "<is><t xml:space=\"preserve\"> </t></is>",
                "<v>#N/A</v>", "<v>#N/A</v>", "<is><t>#N/A</t></is>", "<v>1900-01-01</v>",
            };
            var types = new[] { "", "", "inlineStr", "b", "inlineStr", "inlineStr", "inlineStr", "", "", "str", "inlineStr", "e", "e", "inlineStr", "d" };
            var rows = string.Concat(cells.Select((cell, index) => $"<row r=\"{index + 1}\"><c r=\"A{index + 1}\" {(types[index] == "" ? "" : "t=\"" + types[index] + "\"")}>{cell}</c></row>"));
            using var input = Book(rows, Rule("duplicateValues", "", ranges: "A1:A15"));
            Assert.Equal("110011011001101", Painted(Read(input), 15));
            using var unique = Book(rows, Rule("uniqueValues", "", ranges: "A1:A15"));
            Assert.Equal("001100100110010", Painted(Read(unique), 15));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("top10", "rank=\"1\"")]
    [InlineData("aboveAverage", "")]
    public void NumericPopulationsDoNotCoerceTextBooleanBlankOrErrors(string type, string attributes)
    {
        var rows = NumericRows("0,10,20") + "<row r=\"4\"><c r=\"A4\" t=\"inlineStr\"><is><t>1000</t></is></c></row><row r=\"5\"><c r=\"A5\" t=\"b\"><v>1</v></c></row><row r=\"6\"><c r=\"A6\" t=\"e\"><v>#N/A</v></c></row><row r=\"7\"><c r=\"A7\"/></row>";
        using var input = Book(rows, Rule(type, "", ranges: "A1:A7", extra: attributes));
        Assert.Equal("0010000", Painted(Read(input), 7));
    }

    [Theory]
    [InlineData("percent")]
    [InlineData("percentile")]
    public void PercentAndPercentileUseDifferentIndependentThresholds(string midpoint)
    {
        using var input = Book(NumericRows("0,1,2,100"), Scale("<cfvo type=\"min\"/><cfvo type=\"" + midpoint + "\" val=\"50\"/><cfvo type=\"max\"/>", "000000", "00FF00", "FFFFFF", ranges: "A1:A4"));
        var sheet = Read(input);
        // 50 percent is 50; PERCENTILE.INC(50%) is 1.5. Values below/above that midpoint use different segments.
        Assert.Equal(new ReportColor(0, (byte)(midpoint == "percent" ? 5 : 170), 0), sheet.Cells[new(2, 1)].Style.Background);
        Assert.Equal(midpoint == "percent" ? new ReportColor(0, 10, 0) : new ReportColor(1, 255, 1), sheet.Cells[new(3, 1)].Style.Background);
        Assert.Equal(new ReportColor(255, 255, 255), sheet.Cells[new(4, 1)].Style.Background);
    }

    [Fact]
    public void TwoColorScaleClampsAndRoundsSrgbOnlyAtTheEnd()
    {
        using var input = Book(NumericRows("-1,0,25,50,100,101"), Scale("<cfvo type=\"num\" val=\"0\"/><cfvo type=\"num\" val=\"100\"/>", "000000", "FFFFFF", ranges: "A1:A6"));
        var sheet = Read(input);
        var expected = new byte[] { 0, 0, 64, 128, 255, 255 };
        Assert.Equal(expected.Select(value => (ReportColor?)new ReportColor(value, value, value)), Enumerable.Range(1, 6).Select(row => sheet.Cells[new(row, 1)].Style.Background));
    }

    [Fact]
    public void FormulaThresholdsUseTheSavedAnchorOnceAndThemeIndexedTintResolve()
    {
        var scale = Scale("<cfvo type=\"formula\" val=\"$B1\"/><cfvo type=\"formula\" val=\"B$2\"/>", "000000", "FFFFFF", ranges: "A1:A3");
        using var input = Book("<row r=\"1\"><c r=\"A1\"><v>0</v></c><c r=\"B1\"><v>0</v></c></row><row r=\"2\"><c r=\"A2\"><v>50</v></c><c r=\"B2\"><v>100</v></c></row><row r=\"3\"><c r=\"A3\"><v>100</v></c></row>", scale.Replace("rgb=\"FF000000\"", "indexed=\"0\"").Replace("rgb=\"FFFFFFFF\"", "theme=\"0\" tint=\"1\""));
        using (var package = SpreadsheetDocument.Open(input, true))
        {
            var part = package.WorkbookPart!.AddNewPart<ThemePart>();
            using var data = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("<a:theme xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><a:themeElements><a:clrScheme name=\"test\"><a:lt1><a:srgbClr val=\"000000\"/></a:lt1></a:clrScheme></a:themeElements></a:theme>"));
            part.FeedData(data);
        }

        Assert.Equal(new ReportColor(128, 128, 128), Read(input).Cells[new(2, 1)].Style.Background);
    }

    [Theory]
    [InlineData("<cfvo type=\"min\"/><cfvo type=\"max\"/>", "7,7")]
    [InlineData("<cfvo type=\"num\" val=\"10\"/><cfvo type=\"num\" val=\"0\"/>", "0,10")]
    [InlineData("<cfvo type=\"percentile\" val=\"101\"/><cfvo type=\"max\"/>", "0,10")]
    [InlineData("<cfvo type=\"formula\" val=\"TRUE\"/><cfvo type=\"max\"/>", "0,10")]
    public void InvalidOrCollapsedThresholdsRetainOrdinaryBackground(string points, string numbers)
    {
        using var input = Book(NumericRows(numbers), Scale(points, "000000", "FFFFFF", ranges: "A1:A2") + Rule("expression", "<formula>TRUE</formula>", priority: 2, ranges: "A1:A2"));
        var sheet = Read(input);
        Assert.Equal("00", Painted(sheet, 2));
        Assert.True(sheet.CellReadDiagnostics.Any(diagnostic => diagnostic.Code == "ConditionalFormattingThresholdInvalid") || SourceFeatures(input).Any(feature => feature.Code == "ConditionalFormattingThresholdInvalid"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DynamicAndStaticFillSharePriorityAndFontOnlyAllowsLowerFill(bool dynamicFirst)
    {
        var scale = Scale("<cfvo type=\"min\"/><cfvo type=\"max\"/>", "000000", "FFFFFF", ranges: "A1:A3", priority: dynamicFirst ? 1 : 2);
        using var input = Book(NumericRows("0,50,100"), scale + Rule("expression", "<formula>TRUE</formula>", ranges: "A1:A3", priority: dynamicFirst ? 2 : 1));
        Assert.Equal(dynamicFirst ? new ReportColor(128, 128, 128) : new ReportColor(255, 255, 0), Read(input).Cells[new(2, 1)].Style.Background);
        using var fontOnly = Book(NumericRows("0,50,100"), scale.Replace("priority=\"1\"", "priority=\"2\"") + Rule("expression", "<formula>TRUE</formula>", priority: 1, dxf: 1, ranges: "A1:A3"), [Yellow, Red]);
        var cell = Read(fontOnly).Cells[new(2, 1)];
        Assert.Equal(new ReportColor(128, 128, 128), cell.Style.Background);
        Assert.Equal(new ReportColor(255, 0, 0), cell.Style.Font.Color);
    }

    [Fact]
    public void ExplicitFillClearAndStopReserveTheirHigherPriority()
    {
        var scale = Scale("<cfvo type=\"min\"/><cfvo type=\"max\"/>", "000000", "FFFFFF", ranges: "A1:A3", priority: 2);
        using var cleared = Book(NumericRows("0,50,100"), Rule("expression", "<formula>TRUE</formula>", priority: 1, ranges: "A1:A3") + scale, ["<fill><patternFill patternType=\"none\"/></fill>"]);
        Assert.Null(Read(cleared).Cells[new(2, 1)].Style.Background);
        using var stopped = Book(NumericRows("0,50,100"), Rule("expression", "<formula>TRUE</formula>", priority: 1, dxf: null, ranges: "A1:A3", extra: "stopIfTrue=\"1\"") + scale);
        Assert.Null(Read(stopped).Cells[new(2, 1)].Style.Background);
    }

    [Theory]
    [InlineData("duplicateValues", "", "1,1,2", true)]
    [InlineData("uniqueValues", "", "1,2,2", true)]
    [InlineData("top10", "rank=\"1\"", "100,90,90", true)]
    [InlineData("aboveAverage", "", "20,0,10", true)]
    public void PopulationIsWholeUnionAndDoesNotFollowSelectionOrHiddenRows(string type, string attributes, string values, bool expected)
    {
        using var input = Book(NumericRows(values).Replace("r=\"2\">", "r=\"2\" hidden=\"1\">"), Rule(type, "", ranges: "A1:A2 A2:A3 A1", extra: attributes));
        var sheet = new ExcelReader().Read(input, new() { GetSelectedRange = _ => (1, 1, 1, 1) }).Sheets[0];
        Assert.Equal(expected, sheet.Cells[new(1, 1)].Style.Background is not null);
    }

    [Fact]
    public void OverlappingSqrefsDoNotTurnAUniqueCoordinateIntoADuplicate()
    {
        using var input = Book(NumericRows("1,2,3"), Rule("uniqueValues", "", ranges: "A1:A2 A2:A3 A1:A3"));
        Assert.Equal("111", Painted(Read(input), 3));
    }

    [Fact]
    public void HiddenOffSelectionFormulasUseTheSameValuePolicyAsDisplay()
    {
        var rows = "<row r=\"1\"><c r=\"A1\"><v>10</v></c></row><row r=\"2\" hidden=\"1\"><c r=\"A2\"><f>A1*2</f><v>0</v></c></row>";
        using var input = Book(rows, Rule("top10", "", ranges: "A1:A2", extra: "rank=\"1\""));
        var reader = new ExcelReader();
        var cached = reader.Read(input, new() { GetSelectedRange = _ => (1, 1, 1, 1) }).Sheets[0];
        Assert.NotNull(cached.Cells[new(1, 1)].Style.Background);
        input.Position = 0;
        var recalculated = reader.Read(input, new() { FormulaDisplayMode = FormulaDisplayMode.Recalculate, GetSelectedRange = _ => (1, 1, 1, 1) }).Sheets[0];
        Assert.Null(recalculated.Cells[new(1, 1)].Style.Background);
    }

    [Theory]
    [InlineData("top10")]
    [InlineData("aboveAverage")]
    [InlineData("duplicateValues")]
    [InlineData("uniqueValues")]
    [InlineData("colorScale")]
    public void MissingOffSelectionCacheInvalidatesTheWholePopulation(string type)
    {
        var rule = type == "colorScale" ? Scale("<cfvo type=\"min\"/><cfvo type=\"max\"/>", "000000", "FFFFFF", ranges: "A1:A2") : Rule(type, "", ranges: "A1:A2");
        using var input = Book("<row r=\"1\"><c r=\"A1\"><v>10</v></c></row><row r=\"2\"><c r=\"A2\"><f>1+1</f></c></row>", rule + Rule("expression", "<formula>TRUE</formula>", priority: 2, ranges: "A1"));
        var sheet = new ExcelReader().Read(input, new() { GetSelectedRange = _ => (1, 1, 1, 1) }).Sheets[0];
        Assert.Null(sheet.Cells[new(1, 1)].Style.Background);
        Assert.Contains(sheet.CellReadDiagnostics, diagnostic => diagnostic.Code == "ConditionalFormattingCachedValueMissing");
    }

    [Fact]
    public void WholeSheetBlanksAreCountedByAreaWithoutMaterializingCells()
    {
        using var input = Book("<row r=\"1\"><c r=\"A1\"/></row>", Rule("duplicateValues", "", ranges: "A1:XFD1048576"));
        var sheet = Read(input);
        Assert.Single(sheet.Cells);
        Assert.NotNull(sheet.Cells[new(1, 1)].Style.Background);
    }

    [Fact]
    public void SharedRangesReusePopulationAndSortAndUnreachedRulesStayLazy()
    {
        var metrics = new Dictionary<string, double>();
        ConversionMetrics.Observer = (name, value) => metrics[name] = metrics.GetValueOrDefault(name) + value;
        try
        {
            var scale = Scale("<cfvo type=\"min\"/><cfvo type=\"percentile\" val=\"50\"/><cfvo type=\"max\"/>", "000000", "00FF00", "FFFFFF", ranges: "A1:A3", priority: 3);
            using var input = Book(NumericRows("0,10,20"), Rule("top10", "", priority: 1, ranges: "A1:A2 A2:A3", extra: "rank=\"1\"") + Rule("aboveAverage", "", priority: 2, ranges: "A1:A3") + scale + Rule("top10", "", priority: 4, ranges: "Z1:Z1048576"));
            Read(input);
            Assert.Equal(1, metrics["conditionalFormatting.populations"]);
            Assert.Equal(1, metrics["conditionalFormatting.sorts"]);
            Assert.Equal(3, metrics["conditionalFormatting.valueReads"]);
        }
        finally
        {
            ConversionMetrics.Observer = null;
        }
    }

    [Fact]
    public void RangeNormalizationBudgetsAndCancellationFailWithoutExpandingCoordinates()
    {
        var range = ExcelRenderer.OpenXml.Reader.Model.Cells.CellRange.Parse("A1:XFD1048576");
        Assert.Single(ConditionalPopulation.Normalize([range, range], default).Ranges);
        Assert.Throws<ConditionalFormattingException>(() => ConditionalPopulation.Normalize(Enumerable.Repeat(range, ConditionalPopulation.RangeLimit + 1).ToArray(), default));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => ConditionalPopulation.Normalize([range], cancellation.Token));
    }

    [Theory]
    [InlineData("top10")]
    [InlineData("aboveAverage")]
    [InlineData("colorScale")]
    public void EmptyNumericPopulationsIgnoreRealErrorsWithoutBecomingEvaluationFailures(string type)
    {
        var rule = type == "colorScale" ? Scale("<cfvo type=\"min\"/><cfvo type=\"max\"/>", "000000", "FFFFFF", ranges: "A1:A3") : Rule(type, "", ranges: "A1:A3");
        using var input = Book("<row r=\"1\"><c r=\"A1\" t=\"e\"><v>#N/A</v></c></row><row r=\"2\"><c r=\"A2\" t=\"inlineStr\"><is><t>1</t></is></c></row><row r=\"3\"><c r=\"A3\"/></row>", rule);
        var sheet = Read(input);
        Assert.Equal("000", Painted(sheet, 3));
        Assert.Empty(sheet.CellReadDiagnostics);
    }

    [Fact]
    public void ThreeColorEndpointsMidpointAndQuarterPointsHaveHandCalculatedColors()
    {
        using var input = Book(NumericRows("0,25,50,75,100"), Scale("<cfvo type=\"num\" val=\"0\"/><cfvo type=\"num\" val=\"50\"/><cfvo type=\"num\" val=\"100\"/>", "FF0000", "00FF00", "0000FF", ranges: "A1:A5"));
        var sheet = Read(input);
        Assert.Equal(new ReportColor?[] { new(255, 0, 0), new(128, 128, 0), new(0, 255, 0), new(0, 128, 128), new(0, 0, 255) }, Enumerable.Range(1, 5).Select(row => sheet.Cells[new(row, 1)].Style.Background));
    }

    [Fact]
    public void MergedLeaderDoesNotMultiplyItsValueByMergedArea()
    {
        using var input = Book(NumericRows("1,1,2"), Rule("top10", "", ranges: "A1:B3", extra: "percent=\"1\" rank=\"50\""));
        using (var package = SpreadsheetDocument.Open(input, true))
        {
            var sheet = package.WorkbookPart!.WorksheetParts.Single().Worksheet;
            sheet.InsertAfter(new S.MergeCells(new S.MergeCell { Reference = "A3:B3" }), sheet.GetFirstChild<S.SheetData>());
        }

        // Three numeric coordinates, ceil(3*50%)=2; ties at 1 include both 1s.
        Assert.Equal("111", Painted(Read(input), 3));
    }

    [Fact]
    public void RepeatedTitlesAndPrintAreaDoNotChangeDuplicatePopulation()
    {
        using var input = Book(NumericRows("1,2,3"), Rule("uniqueValues", "", ranges: "A1:A3"));
        using (var package = SpreadsheetDocument.Open(input, true))
        {
            package.WorkbookPart!.Workbook.Append(new S.DefinedNames(
                new S.DefinedName { Name = "_xlnm.Print_Area", LocalSheetId = 0, Text = "data!$A$2:$A$3" },
                new S.DefinedName { Name = "_xlnm.Print_Titles", LocalSheetId = 0, Text = "data!$1:$1" }));
        }

        Assert.Equal("111", Painted(Read(input), 3));
    }

    [Fact]
    public void OffSheetThresholdFormulaUsesSavedPolicyAndAbsoluteMixedReferences()
    {
        using var input = Book(NumericRows("0,50,100"), Scale("<cfvo type=\"formula\" val=\"Second!$A1\"/><cfvo type=\"formula\" val=\"Second!B$1\"/>", "000000", "FFFFFF", ranges: "A1:A3"));
        using (var package = SpreadsheetDocument.Open(input, true))
        {
            TestFixtures.SdkWorkbook.AddSheet(package.WorkbookPart!, new S.Worksheet(new S.SheetData(new S.Row(
                new S.Cell(new S.CellFormula("5+5"), new S.CellValue("0")) { CellReference = "A1" },
                new S.Cell(new S.CellFormula("100+100"), new S.CellValue("100")) { CellReference = "B1" }) { RowIndex = 1 })), "Second");
        }

        Assert.Equal(new ReportColor(128, 128, 128), Read(input).Cells[new(2, 1)].Style.Background);
        input.Position = 0;
        Assert.Equal(new ReportColor(54, 54, 54), new ExcelReader().Read(input, new() { FormulaDisplayMode = FormulaDisplayMode.Recalculate }).Sheets[0].Cells[new(2, 1)].Style.Background);
    }

    [Fact]
    public void AggregateVisitBudgetDoesNotUseAnIncompletePopulationOrLowerPriorityMatch()
    {
        const int size = 20000;
        var rows = string.Concat(Enumerable.Range(1, size).Select(row => $"<row r=\"{row}\"><c r=\"A{row}\"><v>{row}</v></c></row>"));
        var rules = string.Concat(Enumerable.Range(1, 101).Select(index => Rule("aboveAverage", "", priority: index, ranges: $"A1:A{size} Z{index}"))) + Rule("expression", "<formula>TRUE</formula>", priority: 102, ranges: "A1");
        using var input = Book(rows, rules);
        var sheet = new ExcelReader().Read(input, new() { GetSelectedRange = _ => (1, 1, 1, 1) }).Sheets[0];
        Assert.Null(sheet.Cells[new(1, 1)].Style.Background);
        Assert.Contains(sheet.CellReadDiagnostics, diagnostic => diagnostic.Code == "ConditionalFormattingLimitExceeded");
    }

    [Fact]
    public void RangeNormalizationWorkBudgetAndStatisticsOverflowAreDiagnosed()
    {
        var ranges = Enumerable.Range(1, 1024).Select(row => new OpenXml.Reader.Model.Cells.CellRange(new(row * 2, 1), new(row * 2, 2))).ToArray();
        Assert.Throws<ConditionalFormattingException>(() => ConditionalPopulation.Normalize(ranges, default));
        using var input = Book(NumericRows("1E308,-1E308"), Rule("aboveAverage", "", ranges: "A1:A2"));
        var sheet = Read(input);
        Assert.Equal("00", Painted(sheet, 2));
        Assert.Contains(sheet.CellReadDiagnostics, diagnostic => diagnostic.Code == "ConditionalFormattingPopulationInvalid");
    }

    [Fact]
    public void CoalescingAcceptsOneHundredThousandSavedRowIntervals()
    {
        var rows = Enumerable.Range(1, 100000).Select(row => new OpenXml.Reader.Model.Cells.CellRange(new(row, 1), new(row, 1))).ToArray();
        Assert.Equal("A1:A100000", Assert.Single(ConditionalPopulation.Normalize(rows, default).Ranges).ToString());
        var fragmented = Enumerable.Range(1, ConditionalPopulation.SweepRangeLimit + 1).Select(row => new OpenXml.Reader.Model.Cells.CellRange(new(row * 2, 1), new(row * 2, 1))).ToArray();
        Assert.Contains("after coalescing", Assert.Throws<ConditionalFormattingException>(() => ConditionalPopulation.Normalize(fragmented, default)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckerboardUnionHitsRectangleBudgetBeforeAnyCellExpansion()
    {
        var vertical = Enumerable.Range(0, 300).Select(index => new OpenXml.Reader.Model.Cells.CellRange(new(1, (index * 2) + 1), new(600, (index * 2) + 1)));
        var horizontal = Enumerable.Range(0, 300).Select(index => new OpenXml.Reader.Model.Cells.CellRange(new((index * 2) + 1, 1), new((index * 2) + 1, 600)));
        var failure = Assert.Throws<ConditionalFormattingException>(() => ConditionalPopulation.Normalize(vertical.Concat(horizontal).ToArray(), default));
        Assert.Equal("ConditionalFormattingLimitExceeded", failure.Code);
        Assert.Contains("65536", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PopulationCollectionNumericPreparationAndKeyCreationPropagateCancellation()
    {
        using var input = Book(NumericRows("0,10,20"), "");
        using var reader = OpenXmlWorkbookReader.Open(input);
        var calculation = WorkbookCalculationSession.CreateSavedValueSession(reader);
        var ranges = new[] { OpenXml.Reader.Model.Cells.CellRange.Parse("A1:A3") };
        using var cancellation = new CancellationTokenSource();
        var visits = 0;
        Assert.Throws<OperationCanceledException>(() => ConditionalPopulation.Create(ranges, calculation, "data", () => { if (++visits == 2) cancellation.Cancel(); }, cancellation.Token));
        var complete = ConditionalPopulation.Create(ranges, calculation, "data", () => { }, default);
        Assert.Throws<OperationCanceledException>(() => complete.PrepareNumbers(true, cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => complete.Frequency(FormulaValue.FromNumber(10), cancellation.Token));
    }

    private static IReadOnlyList<UnsupportedFeature> SourceFeatures(MemoryStream input)
    {
        input.Position = 0;
        using var reader = OpenXmlWorkbookReader.Open(input);
        return new ConditionalFormattingSession(reader.ReadWorksheetInfo(1), null, new(), _ => true).Features;
    }

    private static string NumericRows(string values) => string.Concat(values.Split(',').Select((value, index) => $"<row r=\"{index + 1}\"><c r=\"A{index + 1}\"><v>{value}</v></c></row>"));

    private static string Painted(ReportSheet sheet, int count) => string.Concat(Enumerable.Range(1, count).Select(row => sheet.Cells.TryGetValue(new(row, 1), out var cell) && cell.Style.Background is not null ? "1" : "0"));

    private static string Scale(string points, string first, string second, string? third = null, string ranges = "A1:A3", int priority = 1) => Rule("colorScale", "<colorScale>" + points + $"<color rgb=\"FF{first}\"/><color rgb=\"FF{second}\"/>" + (third is null ? "" : $"<color rgb=\"FF{third}\"/>") + "</colorScale>", priority, null, ranges);
}
