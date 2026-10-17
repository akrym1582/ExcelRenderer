using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.Excel;
using ExcelRenderer.Rendering;
using ExcelRenderer.TestFixtures;
using Xunit;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.Tests;

public sealed class FormulaRecalculationTests
{
    [Theory]
    [InlineData(OutputFormat.Pdf)]
    [InlineData(OutputFormat.Png)]
    [InlineData(OutputFormat.Svg)]
    [InlineData(OutputFormat.Markdown)]
    public async Task ExpandedFunctionsRenderThroughEveryOutput(OutputFormat format)
    {
        using var input = CreateWorkbook();
        using (var package = SpreadsheetDocument.Open(input, true))
        {
            var cells = package.WorkbookPart!.WorksheetParts.Single().Worksheet.Descendants<S.Cell>().ToArray();
            cells[0].CellFormula = new("COUNTIF(B1:B3,\"\")");
            cells[1].CellFormula = new("LEN(TEXT(DATE(2026,10,17),\"yyyy-mm-dd\"))");
            cells[2].CellFormula = new("TEXT(EOMONTH(DATE(2026,1,31),1),\"yyyy-mm-dd\")");
            cells[3].CellFormula = new("SUMIF(B1:B3,0)");
        }
        input.Position = 0;
        var sheet = new ExcelReader().Read(input, new() { FormulaDisplayMode = FormulaDisplayMode.Recalculate }).Sheets[0];
        Assert.Equal("3", sheet.Cells[new(1, 1)].Text);
        Assert.Equal("10", sheet.Cells[new(2, 1)].Text);
        Assert.Equal("2026-02-28", sheet.Cells[new(3, 1)].Text);
        input.Position = 0;
        using var output = new MemoryStream();
        await ExcelConverter.RenderAsync(input, new() { OutputFormat = format, ReadOptions = new() { FormulaDisplayMode = FormulaDisplayMode.Recalculate } }, new SingleStreamOutputSink(output));
        Assert.NotEmpty(output.ToArray());
        if (format == OutputFormat.Markdown) Assert.Contains("2026-02-28", System.Text.Encoding.UTF8.GetString(output.ToArray()));
    }

    [Theory]
    [InlineData(ReaderBackend.OpenXml)]
    [InlineData(ReaderBackend.Auto)]
    public void RecalculationReplacesStaleCacheBeforeFormatting(ReaderBackend backend)
    {
        using var input = CreateWorkbook();
        var sheet = new ExcelReader().Read(input, new() { Backend = backend, FormulaDisplayMode = FormulaDisplayMode.Recalculate }).Sheets[0];
        Assert.Equal("6", sheet.Cells[new(1, 1)].Text);
        Assert.Equal("TRUE", sheet.Cells[new(2, 1)].Text);
        Assert.Equal("done", sheet.Cells[new(3, 1)].Text);
        Assert.Equal("#DIV/0!", sheet.Cells[new(4, 1)].Text);
        Assert.Equal(Model.HorizontalAlignment.Right, sheet.Cells[new(1, 1)].Style.HorizontalAlignment);
        Assert.Equal(Model.HorizontalAlignment.Center, sheet.Cells[new(2, 1)].Style.HorizontalAlignment);
        Assert.Equal(Model.HorizontalAlignment.Left, sheet.Cells[new(3, 1)].Style.HorizontalAlignment);
        input.Position = 0;
        Assert.Equal("999", new ExcelReader().Read(input).Sheets[0].Cells[new(1, 1)].Text);
    }

    [Theory]
    [InlineData(OutputFormat.Pdf)]
    [InlineData(OutputFormat.Png)]
    [InlineData(OutputFormat.Svg)]
    [InlineData(OutputFormat.Markdown)]
    public async Task RecalculationRendersThroughEveryOutput(OutputFormat format)
    {
        using var input = CreateWorkbook();
        using var output = new MemoryStream();
        await ExcelConverter.RenderAsync(input, new() { OutputFormat = format, ReadOptions = new() { FormulaDisplayMode = FormulaDisplayMode.Recalculate } }, new SingleStreamOutputSink(output));
        Assert.NotEmpty(output.ToArray());
        if (format == OutputFormat.Markdown)
        {
            var text = System.Text.Encoding.UTF8.GetString(output.ToArray());
            Assert.Contains("done", text);
            Assert.DoesNotContain("999", text);
        }
    }

    [Fact]
    public async Task ExplicitRangeDoesNotEvaluateUnrelatedCycleButFollowsDependencies()
    {
        using var input = CreateWorkbook();
        using (var package = SpreadsheetDocument.Open(input, true))
        {
            var cells = package.WorkbookPart!.WorksheetParts.Single().Worksheet.Descendants<S.Cell>().ToArray();
            cells[0].CellFormula = new("A2*3");
            cells[1].CellFormula = new("2");
            cells[2].CellFormula = new("A3");
        }
        input.Position = 0;
        using var output = new MemoryStream();
        await ExcelConverter.RenderAsync(input, new()
        {
            OutputFormat = OutputFormat.Svg,
            ReadOptions = new() { FormulaDisplayMode = FormulaDisplayMode.Recalculate },
            Selection = new() { Ranges = [new("Sheet1", new(new(1, 1), new(1, 1)))] },
        }, new SingleStreamOutputSink(output));
        Assert.NotEmpty(output.ToArray());
    }

    [Fact]
    public async Task UnsupportedFormulaFailsBeforeWritingOutput()
    {
        using var input = CreateWorkbook();
        using (var package = SpreadsheetDocument.Open(input, true))
        {
            package.WorkbookPart!.WorksheetParts.Single().Worksheet.Descendants<S.Cell>().First().CellFormula = new("UNKNOWN()");
        }
        input.Position = 0;
        using var output = new MemoryStream();
        output.Write([1, 2, 3]);
        var exception = await Assert.ThrowsAsync<ConversionException>(() => ExcelConverter.RenderAsync(input,
            new() { OutputFormat = OutputFormat.Svg, ReadOptions = new() { FormulaDisplayMode = FormulaDisplayMode.Recalculate } }, new SingleStreamOutputSink(output)));
        Assert.IsType<OpenXml.Calculation.FormulaCalculationException>(exception.InnerException);
        Assert.Contains("A1", exception.InnerException!.Message);
        Assert.Equal(new byte[] { 1, 2, 3 }, output.ToArray());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InvisibleCycleIsNotEvaluated(bool hiddenRow)
    {
        using var input = CreateWorkbook();
        using (var package = SpreadsheetDocument.Open(input, true))
        {
            var workbook = package.WorkbookPart!;
            var sheet = workbook.WorksheetParts.Single().Worksheet;
            sheet.Descendants<S.Cell>().Last().CellFormula = new("A4");
            if (hiddenRow) sheet.Descendants<S.Row>().Last().Hidden = true;
            else workbook.Workbook.DefinedNames = new S.DefinedNames(new S.DefinedName("Sheet1!$A$1:$A$3") { Name = "_xlnm.Print_Area", LocalSheetId = 0 });
        }
        input.Position = 0;
        var sheetResult = new ExcelReader().Read(input, new() { FormulaDisplayMode = FormulaDisplayMode.Recalculate }).Sheets[0];
        Assert.Equal("6", sheetResult.Cells[new(1, 1)].Text);
    }

    [Theory]
    [InlineData(OutputFormat.Pdf, "A4")]
    [InlineData(OutputFormat.Pdf, "UNKNOWN()")]
    [InlineData(OutputFormat.Png, "A4")]
    [InlineData(OutputFormat.Png, "UNKNOWN()")]
    [InlineData(OutputFormat.Svg, "A4")]
    [InlineData(OutputFormat.Svg, "UNKNOWN()")]
    public async Task PageSelectionSkipsUnrelatedCycleUsingGeometryBeforeCalculation(OutputFormat format, string excludedFormula)
    {
        using var input = CreateWorkbook();
        using (var package = SpreadsheetDocument.Open(input, true))
        {
            var sheet = package.WorkbookPart!.WorksheetParts.Single().Worksheet;
            sheet.Descendants<S.Cell>().Last().CellFormula = new(excludedFormula);
            sheet.Append(new S.RowBreaks(new S.Break { Id = 3, Max = 16383, ManualPageBreak = true }) { Count = 1, ManualBreakCount = 1 });
        }
        input.Position = 0;
        using var output = new MemoryStream();
        var result = await ExcelConverter.RenderAsync(input, new()
        {
            OutputFormat = format,
            ReadOptions = new() { FormulaDisplayMode = FormulaDisplayMode.Recalculate },
            Selection = new() { Pages = [1] },
        }, new SingleStreamOutputSink(output));
        Assert.NotEmpty(output.ToArray());
        Assert.Single(result.Pages);

    }

    [Fact]
    public async Task MarkdownRecalculatesVisibleCellsOutsidePrintArea()
    {
        using var input = CreateWorkbook();
        using (var package = SpreadsheetDocument.Open(input, true))
        {
            var workbook = package.WorkbookPart!;
            workbook.Workbook.DefinedNames = new S.DefinedNames(new S.DefinedName("Sheet1!$A$1:$A$2") { Name = "_xlnm.Print_Area", LocalSheetId = 0 });
        }
        input.Position = 0;
        using var output = new MemoryStream();
        await ExcelConverter.RenderAsync(input, new()
        {
            OutputFormat = OutputFormat.Markdown,
            ReadOptions = new() { FormulaDisplayMode = FormulaDisplayMode.Recalculate },
        }, new SingleStreamOutputSink(output));
        Assert.Contains("done", System.Text.Encoding.UTF8.GetString(output.ToArray()));
        Assert.DoesNotContain("999", System.Text.Encoding.UTF8.GetString(output.ToArray()));
    }

    [Fact]
    public async Task SelectedPageRecalculatesRepeatedTitleAndBody()
    {
        using var input = CreateWorkbook();
        using (var package = SpreadsheetDocument.Open(input, true))
        {
            var workbook = package.WorkbookPart!;
            var sheet = workbook.WorksheetParts.Single().Worksheet;
            sheet.Descendants<S.Cell>().Last().CellFormula = new("42");
            sheet.Append(new S.RowBreaks(new S.Break { Id = 3, Max = 16383, ManualPageBreak = true }) { Count = 1, ManualBreakCount = 1 });
            workbook.Workbook.DefinedNames = new S.DefinedNames(new S.DefinedName("Sheet1!$1:$1") { Name = "_xlnm.Print_Titles", LocalSheetId = 0 });
        }
        input.Position = 0;
        using var output = new MemoryStream();
        var measurer = new RecordingMeasurer();
        await ExcelConverter.RenderAsync(input, new()
        {
            OutputFormat = OutputFormat.Svg,
            TextMeasurer = measurer,
            ReadOptions = new() { FormulaDisplayMode = FormulaDisplayMode.Recalculate },
            Selection = new() { Pages = [2] },
        }, new SingleStreamOutputSink(output));
        Assert.Contains("6", measurer.Texts);
        Assert.Contains("42", measurer.Texts);
        Assert.DoesNotContain("999", measurer.Texts);
    }

    [Theory]
    [InlineData("ROUNDDOWN(0.29,2)", "0.29")]
    [InlineData("ROUNDUP(0.07,2)", "0.07")]
    [InlineData("ROUND(1.005,2)", "1.01")]
    [InlineData("IF(TRUE,\"-\",\"\")", "-")]
    public async Task CorrectedValuesReachMarkdownRendering(string formula, string expected)
    {
        using var input = CreateWorkbook();
        using (var package = SpreadsheetDocument.Open(input, true))
            package.WorkbookPart!.WorksheetParts.Single().Worksheet.Descendants<S.Cell>().First().CellFormula = new(formula);
        input.Position = 0;
        using var output = new MemoryStream();
        await ExcelConverter.RenderAsync(input, new()
        {
            OutputFormat = OutputFormat.Markdown,
            ReadOptions = new() { FormulaDisplayMode = FormulaDisplayMode.Recalculate },
        }, new SingleStreamOutputSink(output));
        string text = System.Text.Encoding.UTF8.GetString(output.ToArray());
        Assert.Contains(expected, text);
        Assert.DoesNotContain("999", text);
    }

    private sealed class RecordingMeasurer : Abstractions.ITextMeasurer
    {
        internal List<string> Texts { get; } = [];
        public Abstractions.TextSize Measure(string text, Model.FontStyle font, double availableWidth, bool wrap)
        {
            Texts.Add(text);
            return new(text.Length * 6, 12);
        }
    }

    private static MemoryStream CreateWorkbook()
    {
        var rows = new S.SheetData();
        var formulas = new[] { "2*3", "TRUE()", "\"done\"", "1/0" };
        for (var index = 0; index < formulas.Length; index++)
        {
            rows.Append(new S.Row(new S.Cell(new S.CellFormula(formulas[index]), new S.CellValue("999"))
            {
                CellReference = "A" + (index + 1), DataType = S.CellValues.Number,
            }) { RowIndex = (uint)(index + 1) });
        }
        return SdkWorkbook.Create(new S.Worksheet(rows), name: "Sheet1");
    }
}
