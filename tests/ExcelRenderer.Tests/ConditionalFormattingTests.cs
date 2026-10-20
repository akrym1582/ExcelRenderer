using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.Excel;
using ExcelRenderer.Model;
using ExcelRenderer.OpenXml.Reader;
using ExcelRenderer.Rendering;
using ExcelRenderer.TestFixtures;
using Xunit;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.Tests;

public sealed partial class ConditionalFormattingTests
{
    private const string Red = "<font><color rgb=\"FFFF0000\"/></font>";
    private const string Yellow = "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFFFFF00\"/></patternFill></fill>";

    [Theory]
    [InlineData("equal", "2", null, true)]
    [InlineData("notEqual", "2", null, false)]
    [InlineData("greaterThan", "1", null, true)]
    [InlineData("greaterThan", "2", null, false)]
    [InlineData("greaterThanOrEqual", "2", null, true)]
    [InlineData("lessThan", "3", null, true)]
    [InlineData("lessThanOrEqual", "2", null, true)]
    [InlineData("between", "2", "3", true)]
    [InlineData("notBetween", "2", "3", false)]
    [InlineData("notBetween", "3", "4", true)]
    [InlineData("equal", "SUM(1,1)", null, true)]
    public void CellIsOperatorsUseTypedFormulaComparison(string operation, string first, string? second, bool match)
    {
        var formulas = "<formula>" + first + "</formula>" + (second is null ? "" : "<formula>" + second + "</formula>");
        using var input = Book("<row r=\"1\"><c r=\"A1\"><v>2</v></c></row>", Rule("cellIs", formulas, extra: "operator=\"" + operation + "\""));
        Assert.Equal(match, Read(input).Cells[new(1, 1)].Style.Background is not null);
    }

    [Fact]
    public void PriorityIsGlobalAndComposesPropertiesWithStopAndExplicitFalse()
    {
        var rows = "<row r=\"1\"><c r=\"A1\"><v>1</v></c><c r=\"B1\"><v>1</v></c></row>";
        var rules = Rule("expression", "<formula>TRUE</formula>", priority: 3, dxf: 0) +
            Rule("expression", "<formula>TRUE</formula>", priority: 1, dxf: 1, ranges: "A1") +
            Rule("expression", "<formula>TRUE</formula>", priority: 2, dxf: 2);
        using var input = Book(rows, rules, [Yellow, Red + "<fontIgnored/>", "<font><b val=\"0\"/><i/><u val=\"none\"/></font>"]);
        var sheet = Read(input);
        Assert.Equal(new ReportColor(255, 255, 0), sheet.Cells[new(1, 1)].Style.Background);
        Assert.Equal(new ReportColor(255, 0, 0), sheet.Cells[new(1, 1)].Style.Font.Color);
        Assert.False(sheet.Cells[new(1, 1)].Style.Font.Bold);
        Assert.True(sheet.Cells[new(1, 1)].Style.Font.Italic);
        Assert.False(sheet.Cells[new(1, 1)].Style.Font.Underline);
        Assert.NotEqual(sheet.Cells[new(1, 1)].Style.Font.Color, sheet.Cells[new(1, 2)].Style.Font.Color);
        using var stopped = Book(rows, Rule("expression", "<formula>TRUE</formula>", priority: 1, dxf: null, extra: "stopIfTrue=\"1\"") + Rule("expression", "<formula>TRUE</formula>", priority: 2));
        Assert.Null(Read(stopped).Cells[new(1, 1)].Style.Background);
    }

    [Fact]
    public void RelativeMixedAbsoluteAndDiscontiguousReferencesKeepFirstSqrefAnchor()
    {
        const string rows = "<row r=\"2\"><c r=\"A2\"><v>0</v></c><c r=\"D2\"><v>1</v></c></row><row r=\"3\"><c r=\"A3\"><v>0</v></c><c r=\"D3\"><v>-1</v></c></row><row r=\"5\"><c r=\"A5\"><v>0</v></c><c r=\"D5\"><v>-1</v></c></row>";
        using var input = Book(rows, Rule("expression", "<formula>AND($D2&lt;0,D$2=1,$D$2=1)</formula>", ranges: "A2:A3 A5"));
        var sheet = Read(input);
        Assert.Null(sheet.Cells[new(2, 1)].Style.Background);
        Assert.NotNull(sheet.Cells[new(3, 1)].Style.Background);
        Assert.NotNull(sheet.Cells[new(5, 1)].Style.Background);
    }

    [Fact]
    public void CachedAndRecalculatedConditionsShareTheirDisplaySnapshot()
    {
        const string rows = "<row r=\"1\"><c r=\"A1\"><f>1+1</f><v>99</v></c></row>";
        using var input = Book(rows, Rule("expression", "<formula>A1=99</formula>"));
        var cached = Read(input);
        Assert.Equal("99", cached.Cells[new(1, 1)].Text);
        Assert.NotNull(cached.Cells[new(1, 1)].Style.Background);
        input.Position = 0;
        var calculated = new ExcelReader().Read(input, new() { FormulaDisplayMode = FormulaDisplayMode.Recalculate }).Sheets[0];
        Assert.Equal("2", calculated.Cells[new(1, 1)].Text);
        Assert.Null(calculated.Cells[new(1, 1)].Style.Background);
    }

    [Theory]
    [InlineData("containsText", "<c r=\"A1\" t=\"inlineStr\"><is><t>AbC</t></is></c>", "bc", true)]
    [InlineData("notContainsText", "<c r=\"A1\"><v>123</v></c>", "2", false)]
    [InlineData("beginsWith", "<c r=\"A1\"><v>123</v></c>", "1", true)]
    [InlineData("endsWith", "<c r=\"A1\" t=\"b\"><v>1</v></c>", "ue", true)]
    [InlineData("containsText", "<c r=\"A1\" t=\"inlineStr\"><is><t>x</t></is></c>", "", true)]
    [InlineData("containsBlanks", "<c r=\"A1\" t=\"str\"><f>\"\"</f><v/></c>", "", true)]
    [InlineData("containsBlanks", "<c r=\"A1\" t=\"inlineStr\"><is><t xml:space=\"preserve\">   </t></is></c>", "", true)]
    [InlineData("notContainsBlanks", "<c r=\"A1\"><v>0</v></c>", "", true)]
    [InlineData("containsErrors", "<c r=\"A1\" t=\"e\"><v>#N/A</v></c>", "", true)]
    [InlineData("containsErrors", "<c r=\"A1\" t=\"inlineStr\"><is><t>#N/A</t></is></c>", "", false)]
    [InlineData("notContainsErrors", "<c r=\"A1\" t=\"inlineStr\"><is><t>#N/A</t></is></c>", "", true)]
    public void TextBlankAndErrorConditionsPreserveValueTypes(string type, string cell, string text, bool expected)
    {
        using var input = Book("<row r=\"1\">" + cell + "</row>", Rule(type, "", extra: "text=\"" + text + "\""));
        Assert.Equal(expected, Read(input).Cells[new(1, 1)].Style.Background is not null);
    }

    [Fact]
    public void MissingCachedValueAndUnsupportedConditionsRetainOrdinaryStyle()
    {
        using var missing = Book("<row r=\"1\"><c r=\"A1\"><f>1+1</f></c></row>", Rule("containsBlanks", ""));
        var sheet = Read(missing);
        Assert.Null(sheet.Cells[new(1, 1)].Style.Background);
        Assert.Contains(sheet.CellReadDiagnostics, diagnostic => diagnostic.Code == "ConditionalFormattingCachedValueMissing");
        using var unsupported = Book("<row r=\"1\"><c r=\"A1\"><v>1</v></c></row>", Rule("dataBar", "", priority: 1, extra: "stopIfTrue=\"1\"") + Rule("expression", "<formula>TRUE</formula>", priority: 2));
        Assert.Null(Read(unsupported).Cells[new(1, 1)].Style.Background);
        using var unknown = Book("<row r=\"1\"><c r=\"A1\"><v>1</v></c></row>", Rule("expression", "<formula>NO_SUCH_FUNCTION(A1)</formula>"));
        Assert.Contains(Read(unknown).CellReadDiagnostics, diagnostic => diagnostic.Code == "ConditionalFormattingFunctionUnsupported");
    }

    [Fact]
    public void WholeColumnRulesColorMissingPrintCellsWithoutGrowingTheSheet()
    {
        using var input = Book("<row r=\"1\"><c r=\"A1\"><v>1</v></c></row>", Rule("containsBlanks", "", ranges: "A1:D1048576"));
        using (var package = SpreadsheetDocument.Open(input, true))
        {
            package.WorkbookPart!.Workbook.Append(new S.DefinedNames(new S.DefinedName { Name = "_xlnm.Print_Area", LocalSheetId = 0, Text = "data!$A$1:$D$10" }));
        }

        input.Position = 0;
        var sheet = Read(input);
        Assert.Equal(40, sheet.Cells.Count);
        Assert.Null(sheet.Cells[new(1, 1)].Style.Background);
        Assert.NotNull(sheet.Cells[new(10, 4)].Style.Background);
        Assert.Equal(10, sheet.Rows.Count);
        Assert.Equal(4, sheet.Columns.Count);
        input.Position = 0;
        Assert.Single(new ExcelReader().Read(input, new() { ApplyConditionalFormatting = false }).Sheets[0].Cells);
    }

    [Fact]
    public void RichTextAndBorderEdgesReceiveOnlyExplicitOverrides()
    {
        const string rows = "<row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><r><rPr><b/><i/></rPr><t>one</t></r><r><rPr><u/></rPr><t>two</t></r></is></c></row>";
        using var input = Book(rows, Rule("expression", "<formula>TRUE</formula>", dxf: 0) + Rule("expression", "<formula>TRUE</formula>", priority: 2, dxf: 1),
            ["<font><b val=\"0\"/><color rgb=\"FFFF0000\"/></font><border><left style=\"double\"><color indexed=\"4\"/></left><top/></border>", "<border><top style=\"thick\"/><bottom style=\"dashed\"/></border>"]);
        var cell = Read(input).Cells[new(1, 1)];
        Assert.All(cell.RichTextRuns!, run => { Assert.False(run.Font.Bold); Assert.Equal(new ReportColor(255, 0, 0), run.Font.Color); });
        Assert.True(cell.RichTextRuns![0].Font.Italic);
        Assert.True(cell.RichTextRuns[1].Font.Underline);
        Assert.Equal(BorderLineStyle.Double, cell.Style.Border!.Left!.LineStyle);
        Assert.Null(cell.Style.Border.Top);
        Assert.Equal(BorderLineStyle.Dashed, cell.Style.Border.Bottom!.LineStyle);
    }

    [Fact]
    public async Task CoreRetainsNormalStylesAndPdfWhileMainAppliesConditionalFormatting()
    {
        using var input = Book("<row r=\"1\"><c r=\"A1\"><v>1</v></c></row>", Rule("expression", "<formula>TRUE</formula>"));
        var bytes = input.ToArray();
        Assert.NotNull(Read(input).Cells[new(1, 1)].Style.Background);
        using var source = new MemoryStream(bytes);
        using var prepared = await OpenXml.Reader.Input.WorkbookInputPreparer.ReadAsync(source, new(), CancellationToken.None);
        using var font = new Core.Fonts.SingleFontContext(Path.Combine(AppContext.BaseDirectory, "NotoSansJP-Regular.ttf"));
        var core = new Core.Excel.CoreExcelReader(font).Read(prepared, null);
        Assert.Null(core.Sheets[0].Cells[new(1, 1)].Style.Background);
        source.Position = 0;
        using var pdf = new MemoryStream();
        await Core.ExcelConverter.ConvertAsync(source, pdf, new() { FontFilePath = Path.Combine(AppContext.BaseDirectory, "NotoSansJP-Regular.ttf") });
        Assert.True(pdf.Length > 100);
    }

    [Theory]
    [InlineData(OutputFormat.Pdf)]
    [InlineData(OutputFormat.Png)]
    [InlineData(OutputFormat.Svg)]
    public async Task AllRenderFormatsUseTheEffectiveAppearance(OutputFormat format)
    {
        using var input = Book("<row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>Conditional</t></is></c></row>", Rule("expression", "<formula>TRUE</formula>"), [Yellow + Red + "<border><bottom style=\"thick\"/></border>"]);
        using var output = new MemoryStream();
        var result = await ExcelConverter.RenderAsync(input, new() { OutputFormat = format }, new SingleStreamOutputSink(output));
        Assert.True(output.Length > 100);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code.StartsWith("ConditionalFormatting", StringComparison.Ordinal));
        var directory = Environment.GetEnvironmentVariable("EXCELRENDERER_CF_ARTIFACTS");
        if (format is OutputFormat.Png or OutputFormat.Svg)
        {
            using var svg = new Svg.Skia.SKSvg();
            using var svgInput = new MemoryStream(output.ToArray());
            using var bitmap = format == OutputFormat.Png ? global::SkiaSharp.SKBitmap.Decode(output.ToArray()) : Raster(svg.Load(svgInput)!);
            Assert.Contains(bitmap.Pixels, pixel => pixel.Red > 240 && pixel.Green > 240 && pixel.Blue < 20);
            Assert.Contains(bitmap.Pixels, pixel => pixel.Red > 200 && pixel.Green < 80 && pixel.Blue < 80);
            if (directory is not null && format == OutputFormat.Svg)
            {
                Directory.CreateDirectory(directory);
                using var image = bitmap.Encode(global::SkiaSharp.SKEncodedImageFormat.Png, 100);
                await File.WriteAllBytesAsync(Path.Combine(directory, "svg-review.png"), image.ToArray());
            }
        }

        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllBytesAsync(Path.Combine(directory, "conditional-formatting." + format.ToString().ToLowerInvariant()), output.ToArray());
            await File.WriteAllBytesAsync(Path.Combine(directory, "conditional-formatting.xlsx"), input.ToArray());
        }
    }

    [Theory]
    [InlineData(OutputFormat.Pdf)]
    [InlineData(OutputFormat.Png)]
    [InlineData(OutputFormat.Svg)]
    public async Task MappingExpansionPreservesMixedReferencesPrioritiesAndRendering(OutputFormat format)
    {
        using var template = Book("<row r=\"1\"><c r=\"A1\"><v>1</v></c></row><row r=\"2\"><c r=\"A2\" t=\"inlineStr\"><is><t>**items[*]</t></is></c><c r=\"D2\"><v>1</v></c></row>",
            Rule("expression", "<formula>AND($D2=1,D$2=1,$D$2=1)</formula>", ranges: "A2", extra: "stopIfTrue=\"1\"") +
            Rule("expression", "<formula>TRUE</formula>", ranges: "A2", priority: 2, dxf: 1), [Yellow, Red]);
        using var mapped = new MemoryStream();
        Mapping.ExcelTemplateMapper.Map(template, mapped, new { items = new[] { 10, 20, 30 } });
        mapped.Position = 0;
        using (var reader = OpenXmlWorkbookReader.Open(mapped))
        {
            var rules = reader.ReadWorksheetInfo(1).ConditionalFormattingRules;
            var priorities = rules.Select(rule => rule.Source.Attributes["priority"]).ToArray();
            Assert.Equal(priorities.Length, priorities.Distinct().Count());
        }

        var sheet = Read(mapped);
        foreach (var row in new[] { 2, 3, 4 })
        {
            Assert.NotNull(sheet.Cells[new(row, 1)].Style.Background);
            Assert.NotEqual(new ReportColor(255, 0, 0), sheet.Cells[new(row, 1)].Style.Font.Color);
        }

        mapped.Position = 0;
        using var output = new MemoryStream();
        await ExcelConverter.RenderAsync(mapped, new() { OutputFormat = format }, new SingleStreamOutputSink(output));
        Assert.True(output.Length > 100);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SnapshotIsAbsentWithoutRulesOrWhenDisabled(bool hasRules)
    {
        using var input = Book("<row r=\"1\"><c r=\"A1\"><v>1</v></c></row>", hasRules ? Rule("expression", "<formula>TRUE</formula>") : "");
        var snapshots = 0L;
        OpenXml.Reader.Diagnostics.OperationMetrics.Observer = (name, count) => { if (name == "calculation.snapshots") snapshots += count; };
        try
        {
            new ExcelReader().Read(input, new() { ApplyConditionalFormatting = !hasRules });
            Assert.Equal(0, snapshots);
            if (hasRules)
            {
                input.Position = 0;
                new ExcelReader().Read(input);
                Assert.Equal(1, snapshots);
            }

            snapshots = 0;
            input.Position = 0;
            using var pdf = new MemoryStream();
            await Core.ExcelConverter.ConvertAsync(input, pdf, new() { FontFilePath = Path.Combine(AppContext.BaseDirectory, "NotoSansJP-Regular.ttf") });
            Assert.Equal(0, snapshots);
        }
        finally
        {
            OpenXml.Reader.Diagnostics.OperationMetrics.Observer = null;
        }
    }

    [Fact]
    public void ClearFillWinsAndLegitimateFormulaErrorsDoNotBlockLowerRules()
    {
        using var clear = Book("<row r=\"1\"><c r=\"A1\"><v>1</v></c></row>",
            Rule("expression", "<formula>TRUE</formula>", dxf: 1) + Rule("expression", "<formula>TRUE</formula>", priority: 2),
            [Yellow, "<fill><patternFill patternType=\"none\"/></fill>"]);
        Assert.Null(Read(clear).Cells[new(1, 1)].Style.Background);
        using var error = Book("<row r=\"1\"><c r=\"A1\"><v>1</v></c></row>",
            Rule("expression", "<formula>1/0</formula>", extra: "stopIfTrue=\"1\"") + Rule("expression", "<formula>TRUE</formula>", priority: 2));
        Assert.NotNull(Read(error).Cells[new(1, 1)].Style.Background);
    }

    [Theory]
    [InlineData("dataBar", "", 0, "UnsupportedConditionalFormatting")]
    [InlineData("expression", "<formula>TRUE</formula>", 99, "ConditionalFormattingDifferentialInvalid")]
    [InlineData("expression", "<formula>TRUE</formula>", 0, "ConditionalFormattingPriorityInvalid")]
    public async Task UnsupportedRuleDiagnosticsRemainSelectedAndStable(string type, string body, int dxf, string code)
    {
        using var input = Book("<row r=\"1\"><c r=\"A1\"><v>1</v></c></row>", Rule(type, body, priority: code.EndsWith("PriorityInvalid", StringComparison.Ordinal) ? 0 : 1, dxf: dxf));
        using var output = new MemoryStream();
        var result = await ExcelConverter.RenderAsync(input, new() { OutputFormat = OutputFormat.Markdown, DiagnosticOptions = new() { DetectUnsupportedFeatures = true } }, new SingleStreamOutputSink(output));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == code && diagnostic.ObjectId == "conditional-format-1");
    }

    [Fact]
    public async Task DisabledRulesAreInformationalAndDoNotTriggerStrictMode()
    {
        using var input = Book("<row r=\"1\"><c r=\"A1\"><v>1</v></c></row>", Rule("expression", "<formula>TRUE</formula>"));
        using var output = new MemoryStream();
        var result = await ExcelConverter.RenderAsync(input, new()
        {
            OutputFormat = OutputFormat.Markdown,
            ReadOptions = new() { ApplyConditionalFormatting = false, ColumnWidthMetrics = new() { MaximumDigitWidthPixels96Dpi = 7 } },
            DiagnosticOptions = new() { DetectUnsupportedFeatures = true, StrictMode = true },
        }, new SingleStreamOutputSink(output));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "ConditionalFormattingDisabled" && diagnostic.Severity == DiagnosticSeverity.Info);
    }

    [Fact]
    public void MergedLeadersUseEffectiveStyleBeforeBorderAggregation()
    {
        using var input = Book("<row r=\"1\"><c r=\"A1\"><v>1</v></c><c r=\"B1\"><v>2</v></c></row>", Rule("expression", "<formula>TRUE</formula>", ranges: "A1:B1"),
            [Yellow + "<border><left style=\"thick\"/><top style=\"double\"/><right style=\"thin\"/><bottom style=\"dotted\"/></border>"]);
        using (var package = SpreadsheetDocument.Open(input, true))
        {
            package.WorkbookPart!.WorksheetParts.Single().Worksheet.InsertAfter(new S.MergeCells(new S.MergeCell { Reference = "A1:B1" }), package.WorkbookPart.WorksheetParts.Single().Worksheet.GetFirstChild<S.SheetData>());
        }

        var sheet = Read(input);
        var leader = Assert.Single(sheet.Cells).Value;
        Assert.Equal(2, leader.ColumnSpan);
        Assert.NotNull(leader.Style.Background);
        Assert.Contains(leader.MergedBorders!, border => border.Border.Left?.Width == 2);
        Assert.Contains(leader.MergedBorders!, border => border.Address == new CellAddress(1, 2) && border.Border.Right is not null);
        Assert.Equal(2, leader.MergedBorders!.Count(border => border.Border.Bottom?.LineStyle == BorderLineStyle.Dotted));
    }

    [Fact]
    public async Task OffSheetNamedDependenciesAreKeptForSelectedOutput()
    {
        using var input = Book("<row r=\"1\"><c r=\"A1\"><v>1</v></c></row>", Rule("expression", "<formula>AND(Second!A1=7,Threshold=7)</formula>"));
        using (var package = SpreadsheetDocument.Open(input, true))
        {
            SdkWorkbook.AddSheet(package.WorkbookPart!, new S.Worksheet(new S.SheetData(new S.Row(SdkWorkbook.Number("A1", "7")) { RowIndex = 1 })), "Second");
            package.WorkbookPart!.Workbook.Append(new S.DefinedNames(new S.DefinedName { Name = "Threshold", Text = "Second!$A$1" }));
        }

        input.Position = 0;
        using var prepared = await OpenXml.Reader.Input.WorkbookInputPreparer.ReadAsync(input, new(), CancellationToken.None);
        var document = new CoreIntegration.FullExcelReader(null, null, new()).Read(prepared, null, ["data"]);
        Assert.NotNull(document.Sheets.Single(sheet => sheet.Name == "data").Cells[new(1, 1)].Style.Background);
        Assert.Empty(document.Sheets.Single(sheet => sheet.Name == "Second").Cells);
    }

    [Fact]
    public void CommonDrawingCommandsContainEffectiveBackgroundFontAndBorder()
    {
        using var input = Book("<row r=\"1\"><c r=\"A1\"><v>1</v></c></row>", Rule("expression", "<formula>TRUE</formula>"), [Yellow + Red + "<border><bottom style=\"thick\"/></border>"]);
        var sheet = Read(input);
        var measurer = new SkiaSharp.SkiaTextMeasurer(new Fonts.FontManager(new Fonts.FontOptions()));
        var document = new Layout.ReportLayoutEngine(measurer).Layout(sheet);
        var commands = new Drawing.DrawCommandGeneratorPass().Generate(document);
        Assert.Contains(commands, command => command is Drawing.FillRectangleCommand fill && fill.Color == new ReportColor(255, 255, 0));
        Assert.Contains(commands, command => command is Drawing.DrawTextCommand text && text.Style.Font.Color == new ReportColor(255, 0, 0));
        Assert.Contains(commands, command => command is Drawing.DrawLineCommand line && line.Style.Width == 2 && line.Y1 == line.Y2);
    }

    [Fact]
    public async Task PageSelectionIncludesMissingBlanksAndSkipsOffPageFailures()
    {
        using var input = Book("<row r=\"1\"><c r=\"A1\"><v>1</v></c></row>",
            Rule("containsBlanks", "", ranges: "A1:B4") + Rule("expression", "<formula>UNSUPPORTED()</formula>", ranges: "A3:B4", priority: 2));
        using (var package = SpreadsheetDocument.Open(input, true))
        {
            var worksheet = package.WorkbookPart!.WorksheetParts.Single().Worksheet;
            worksheet.Append(new S.RowBreaks(new S.Break { Id = 2, Min = 0, Max = 16383, ManualPageBreak = true }));
            package.WorkbookPart.Workbook.Append(new S.DefinedNames(new S.DefinedName { Name = "_xlnm.Print_Area", LocalSheetId = 0, Text = "data!$A$1:$B$4" }));
        }

        input.Position = 0;
        using var output = new MemoryStream();
        var result = await ExcelConverter.RenderAsync(input, new() { OutputFormat = OutputFormat.Svg, Selection = new() { Pages = [1] } }, new SingleStreamOutputSink(output));
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "ConditionalFormattingFunctionUnsupported");
        Assert.Contains("fill=\"yellow\"", System.Text.Encoding.UTF8.GetString(output.ToArray()), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EvaluationLimitsAndCancellationAreNotSilentNonmatches()
    {
        using var input = Book("<row r=\"1\"><c r=\"A1\"><v>1</v></c></row>", Rule("expression", "<formula>A1=1</formula>"));
        var sheet = new ExcelReader().Read(input, new() { FormulaEvaluationOptions = new() { MaxEvaluationSteps = 1 } }).Sheets[0];
        Assert.Null(sheet.Cells[new(1, 1)].Style.Background);
        Assert.Contains(sheet.CellReadDiagnostics, diagnostic => diagnostic.Code == "ConditionalFormattingLimitExceeded");
        input.Position = 0;
        using var output = new MemoryStream();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ExcelConverter.RenderAsync(input, new() { OutputFormat = OutputFormat.Pdf }, new SingleStreamOutputSink(output), cancellation.Token));
    }

    [Fact]
    public void DifferentialThemeAndIndexedTintUseResolvedColors()
    {
        using var input = Book("<row r=\"1\"><c r=\"A1\"><v>1</v></c></row>", Rule("expression", "<formula>TRUE</formula>"),
            ["<font><color theme=\"4\" tint=\"0.5\"/></font><fill><patternFill patternType=\"solid\"><fgColor indexed=\"2\" tint=\"0.5\"/></patternFill></fill><border><bottom style=\"thin\"><color indexed=\"2\" tint=\"0.5\"/></bottom></border>"]);
        using (var package = SpreadsheetDocument.Open(input, true))
        {
            var part = package.WorkbookPart!.AddNewPart<ThemePart>();
            using var data = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("<a:theme xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><a:themeElements><a:clrScheme name=\"test\"><a:accent1><a:srgbClr val=\"000000\"/></a:accent1></a:clrScheme></a:themeElements></a:theme>"));
            part.FeedData(data);
        }

        var cell = Read(input).Cells[new(1, 1)];
        Assert.Equal(new ReportColor(128, 128, 128), cell.Style.Font.Color);
        Assert.Equal(new ReportColor(255, 128, 128), cell.Style.Background);
        Assert.Equal(cell.Style.Background, cell.Style.Border!.Bottom!.Color);
    }

    [Fact]
    public void RepeatedTitleBlanksAreCandidatesOutsideTheBodyPrintArea()
    {
        using var input = Book("<row r=\"3\"><c r=\"A3\"><v>1</v></c></row>", Rule("containsBlanks", "", ranges: "A1:B1"));
        using (var package = SpreadsheetDocument.Open(input, true))
        {
            package.WorkbookPart!.Workbook.Append(new S.DefinedNames(
                new S.DefinedName { Name = "_xlnm.Print_Area", LocalSheetId = 0, Text = "data!$A$3:$B$4" },
                new S.DefinedName { Name = "_xlnm.Print_Titles", LocalSheetId = 0, Text = "data!$1:$1" }));
        }

        var sheet = Read(input);
        Assert.NotNull(sheet.Cells[new(1, 1)].Style.Background);
        Assert.NotNull(sheet.Cells[new(1, 2)].Style.Background);
        Assert.Null(sheet.Cells[new(3, 1)].Style.Background);
    }

    [Fact]
    public async Task AggregatedEvaluationDiagnosticsUseA1AddressesAndFailBeforeOpeningSink()
    {
        var rows = string.Concat(Enumerable.Range(1, 100).Select(row => $"<row r=\"{row}\"><c r=\"A{row}\"><v>1</v></c></row>"));
        using var input = Book(rows, Rule("expression", "<formula>UNSUPPORTED()</formula>", ranges: "A1:A100"));
        var diagnostic = Assert.Single(Read(input).CellReadDiagnostics);
        Assert.Equal("A1", diagnostic.CellRange);
        Assert.Equal(100, diagnostic.OccurrenceCount);
        input.Position = 0;
        using var output = new MemoryStream();
        output.Write([1, 2, 3]);
        await Assert.ThrowsAsync<ConversionException>(() => ExcelConverter.RenderAsync(input, new()
        {
            OutputFormat = OutputFormat.Pdf,
            DiagnosticOptions = new() { TreatAsErrors = ["ConditionalFormattingFunctionUnsupported"] },
        }, new SingleStreamOutputSink(output)));
        Assert.Equal(new byte[] { 1, 2, 3 }, output.ToArray());
    }

    private static global::SkiaSharp.SKBitmap Raster(global::SkiaSharp.SKPicture picture)
    {
        var bitmap = new global::SkiaSharp.SKBitmap((int)Math.Ceiling(picture.CullRect.Width), (int)Math.Ceiling(picture.CullRect.Height));
        using var canvas = new global::SkiaSharp.SKCanvas(bitmap);
        canvas.Clear(global::SkiaSharp.SKColors.White);
        canvas.DrawPicture(picture);
        return bitmap;
    }

    [Theory]
    [InlineData("<patternFill><bgColor rgb=\"FFFF0000\"/></patternFill>")]
    [InlineData("<patternFill patternType=\"solid\"><fgColor auto=\"1\"/><bgColor rgb=\"FFFF0000\"/></patternFill>")]
    public void DifferentialBackgroundColorDoesNotBecomeAnAutomaticForeground(string pattern)
    {
        using var input = Book("<row r=\"1\"><c r=\"A1\"><v>1</v></c></row>", Rule("expression", "<formula>TRUE</formula>"), ["<fill>" + pattern + "</fill>"]);
        Assert.Equal(new ReportColor(255, 0, 0), Read(input).Cells[new(1, 1)].Style.Background);
    }

    [Fact]
    public void RepeatedRowAndColumnTitlesIncludeTheirBlankCorner()
    {
        using var input = Book("<row r=\"3\"><c r=\"C3\"><v>1</v></c></row>", Rule("containsBlanks", "", ranges: "A1:D1"));
        using (var package = SpreadsheetDocument.Open(input, true))
        {
            package.WorkbookPart!.Workbook.Append(new S.DefinedNames(
                new S.DefinedName { Name = "_xlnm.Print_Area", LocalSheetId = 0, Text = "data!$C$3:$D$4" },
                new S.DefinedName { Name = "_xlnm.Print_Titles", LocalSheetId = 0, Text = "data!$1:$1,data!$A:$A" }));
        }

        var sheet = Read(input);
        Assert.NotNull(sheet.Cells[new(1, 1)].Style.Background);
        Assert.NotNull(sheet.Cells[new(1, 3)].Style.Background);
        Assert.False(sheet.Cells.ContainsKey(new(1, 2)));
    }

    [Fact]
    public void EmptySheetsOnlyColorTheirExistingA1Page()
    {
        using var input = Book("<row r=\"1\"><c r=\"A1\"/></row>", Rule("containsBlanks", "", ranges: "A1:XFD1048576"));
        var sheet = Read(input);
        Assert.Single(sheet.Cells);
        Assert.NotNull(sheet.Cells[new(1, 1)].Style.Background);
        Assert.Single(sheet.Rows);
        Assert.Single(sheet.Columns);
    }

    [Fact]
    public void SelectionInsideAMergeStillEvaluatesItsOriginalLeader()
    {
        using var input = Book("<row r=\"1\"><c r=\"A1\"><v>1</v></c></row>", Rule("expression", "<formula>TRUE</formula>", ranges: "A1"));
        using (var package = SpreadsheetDocument.Open(input, true))
        {
            package.WorkbookPart!.WorksheetParts.Single().Worksheet.InsertAfter(new S.MergeCells(new S.MergeCell { Reference = "A1:C3" }), package.WorkbookPart.WorksheetParts.Single().Worksheet.GetFirstChild<S.SheetData>());
        }

        input.Position = 0;
        var sheet = new ExcelReader().Read(input, new() { GetSelectedRange = _ => (2, 2, 2, 2) }).Sheets[0];
        Assert.NotNull(sheet.Cells[new(1, 1)].Style.Background);
        Assert.Equal(3, sheet.Cells[new(1, 1)].ColumnSpan);
    }

    private static ReportSheet Read(MemoryStream input)
    {
        input.Position = 0;
        return new ExcelReader().Read(input).Sheets[0];
    }

    private static string Rule(string type, string body, int priority = 1, int? dxf = 0, string ranges = "A1:B1", string extra = "") =>
        $"<conditionalFormatting xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" sqref=\"{ranges}\"><cfRule type=\"{type}\" priority=\"{priority}\" {(dxf is null ? "" : "dxfId=\"" + dxf + "\"")} {extra}>{body}</cfRule></conditionalFormatting>";

    private static MemoryStream Book(string rows, string rules, string[]? differentials = null)
    {
        var styles = SdkWorkbook.Styles();
        styles.InnerXml += "<dxfs xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" + string.Concat((differentials ?? [Yellow]).Select(dxf => "<dxf>" + dxf + "</dxf>")) + "</dxfs>";
        var worksheet = new S.Worksheet { InnerXml = "<sheetData xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" + rows + "</sheetData>" + rules };
        return SdkWorkbook.Create(worksheet, styles);
    }
}
