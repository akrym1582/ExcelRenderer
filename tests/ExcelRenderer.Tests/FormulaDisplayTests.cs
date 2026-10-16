using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.Excel;
using ExcelRenderer.Model;
using ExcelRenderer.Rendering;
using Xunit;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.Tests;

public sealed class FormulaDisplayTests
{
    [Theory]
    [InlineData(ReaderBackend.ClosedXml)]
    [InlineData(ReaderBackend.OpenXml)]
    [InlineData(ReaderBackend.Auto)]
    public void CachedModeDistinguishesMissingZeroFalseEmptyTextAndErrorsWithoutEvaluating(ReaderBackend backend)
    {
        using var input = CreateWorkbook();
        var sheet = new ExcelReader().Read(input, Cached(backend)).Sheets[0];
        Assert.Equal("0", sheet.Cells[new(1, 1)].Text);
        Assert.Equal("FALSE", sheet.Cells[new(2, 1)].Text);
        Assert.Equal(string.Empty, sheet.Cells[new(3, 1)].Text);
        Assert.Equal(string.Empty, sheet.Cells[new(4, 1)].Text);
        Assert.Equal("saved", sheet.Cells[new(5, 1)].Text);
        Assert.Equal("#DIV/0!", sheet.Cells[new(6, 1)].Text);
        Assert.Equal(HorizontalAlignment.Right, sheet.Cells[new(1, 1)].Style.HorizontalAlignment);
        Assert.Equal(HorizontalAlignment.Center, sheet.Cells[new(2, 1)].Style.HorizontalAlignment);
        Assert.Equal(HorizontalAlignment.Left, sheet.Cells[new(5, 1)].Style.HorizontalAlignment);
        Assert.Equal("A4", Assert.Single(sheet.CellReadDiagnostics, d => d.Code == "FormulaCachedValueMissing" && d.CellRange == "A4").CellRange);
        Assert.Equal("A6", Assert.Single(sheet.CellReadDiagnostics, d => d.Code == "FormulaResultError").CellRange);
        Assert.Equal("99", sheet.Cells[new(7, 1)].Text);
        Assert.Equal(string.Empty, sheet.Cells[new(8, 1)].Text);
        Assert.Contains(sheet.CellReadDiagnostics, d => d.Code == "FormulaCachedValueMissing" && d.CellRange == "A8");
    }

    [Theory]
    [InlineData(false, "1900-01-02", ReaderBackend.ClosedXml)]
    [InlineData(false, "1900-01-02", ReaderBackend.OpenXml)]
    [InlineData(true, "1904-01-03", ReaderBackend.ClosedXml)]
    [InlineData(true, "1904-01-03", ReaderBackend.OpenXml)]
    public void OriginalDateSerialUsesWorkbookDateSystemOnce(bool use1904, string expected, ReaderBackend backend)
    {
        using var input = CreateWorkbook(use1904);
        var sheet = new ExcelReader().Read(input, Cached(backend)).Sheets[0];
        Assert.Equal(expected, sheet.Cells[new(9, 1)].Text);
        Assert.Equal("25.00%", sheet.Cells[new(10, 1)].Text);
        Assert.Equal("012.50", sheet.Cells[new(11, 1)].Text);
        Assert.Equal("12:00", sheet.Cells[new(12, 1)].Text);
    }

    [Theory]
    [InlineData(ReaderBackend.ClosedXml)]
    [InlineData(ReaderBackend.OpenXml)]
    [InlineData(ReaderBackend.Auto)]
    public void CachedDurationsDoNotAcquireThe1904DateOffset(ReaderBackend backend)
    {
        using var input = CreateWorkbook(true);
        using (var package = SpreadsheetDocument.Open(input, true))
        {
            var sheet = package.WorkbookPart!.WorksheetParts.Single().Worksheet;
            var cell = sheet.Descendants<S.Cell>().Single(c => c.CellReference == "A12");
            var styles = package.WorkbookPart.WorkbookStylesPart!.Stylesheet;
            styles.NumberingFormats!.Append(new S.NumberingFormat { NumberFormatId = 250, FormatCode = "[h]:mm" });
            styles.CellFormats!.Append(new S.CellFormat { NumberFormatId = 250, ApplyNumberFormat = true });
            cell.StyleIndex = (uint)(styles.CellFormats.ChildElements.Count - 1);
            cell.CellValue = new("1.5");
        }
        input.Position = 0;
        Assert.Equal("36:00", new ExcelReader().Read(input, new() { Backend = backend }).Sheets[0].Cells[new(12, 1)].Text);
    }

    [Theory]
    [InlineData(OutputFormat.Pdf, ReaderBackend.ClosedXml)]
    [InlineData(OutputFormat.Pdf, ReaderBackend.OpenXml)]
    [InlineData(OutputFormat.Markdown, ReaderBackend.ClosedXml)]
    [InlineData(OutputFormat.Markdown, ReaderBackend.OpenXml)]
    public async Task MissingCacheDiagnosticIsSelectedAndFailsBeforeSink(OutputFormat format, ReaderBackend backend)
    {
        using var input = CreateWorkbook();
        using var output = new MemoryStream();
        output.Write([1, 2, 3]);
        var request = new RenderRequest
        {
            OutputFormat = format,
            ReadOptions = Cached(backend),
            Selection = format == OutputFormat.Markdown ? new() : new() { Ranges = [new("formulas", new(new(4, 1), new(4, 1)))] },
            DiagnosticOptions = new() { TreatAsErrors = ["FormulaCachedValueMissing"], SuppressedCodes = ["FormulaCachedValueMissing"], MaxDiagnostics = 1 },
        };
        await Assert.ThrowsAsync<ConversionException>(() => ExcelConverter.RenderAsync(input, request, new SingleStreamOutputSink(output)));
        Assert.Equal(new byte[] { 1, 2, 3 }, output.ToArray());
        if (format == OutputFormat.Markdown)
        {
            return;
        }

        input.Position = 0;
        using var selectedOutput = new MemoryStream();
        var result = await ExcelConverter.RenderAsync(input, request with
        {
            Selection = new() { Ranges = [new("formulas", new(new(1, 1), new(1, 1)))] },
        }, new SingleStreamOutputSink(selectedOutput));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code.StartsWith("Formula", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(ReaderBackend.ClosedXml)]
    [InlineData(ReaderBackend.OpenXml)]
    [InlineData(ReaderBackend.Auto)]
    public void SharedAndArrayFormulaFollowersUseTheirOwnOriginalCaches(ReaderBackend backend)
    {
        using var input = CreateWorkbook();
        using (var package = SpreadsheetDocument.Open(input, true))
        {
            var cells = package.WorkbookPart!.WorksheetParts.Single().Worksheet.Descendants<S.Cell>().ToArray();
            cells[0].CellFormula = new S.CellFormula("SUM(1,1)") { FormulaType = S.CellFormulaValues.Shared, SharedIndex = 0, Reference = "A1:A2" };
            cells[1].CellFormula = new S.CellFormula { FormulaType = S.CellFormulaValues.Shared, SharedIndex = 0 };
            cells[2].CellFormula = new S.CellFormula("{1;2}") { FormulaType = S.CellFormulaValues.Array, Reference = "A3:A4" };
            cells[3].CellFormula = null;
            for (var i = 0; i < 4; i++)
            {
                cells[i].DataType = S.CellValues.Number;
                cells[i].CellValue = new S.CellValue((90 + i).ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        input.Position = 0;
        var sheet = new ExcelReader().Read(input, Cached(backend)).Sheets[0];
        for (var row = 1; row <= 4; row++)
        {
            Assert.Equal((89 + row).ToString(System.Globalization.CultureInfo.InvariantCulture), sheet.Cells[new(row, 1)].Text);
            Assert.Equal(row is 1 or 3, sheet.Cells[new(row, 1)].Formula is not null);
        }

        Assert.Equal(0, sheet.Cells[new(2, 1)].FormulaSharedIndex);
        Assert.Equal(new CellAddress(1, 1), sheet.Cells[new(2, 1)].FormulaMasterAddress);
        Assert.Equal(new CellAddress(3, 1), sheet.Cells[new(4, 1)].FormulaMasterAddress);
        Assert.DoesNotContain(sheet.CellReadDiagnostics, d => d.Code == "FormulaCachedValueMissing" && d.CellRange != "A8");
    }

    [Fact]
    public void CachedUnsupportedExternalAndDynamicHyperlinkFormulasDoNotEvaluate()
    {
        using var input = CreateWorkbook();
        using (var package = SpreadsheetDocument.Open(input, true))
        {
            var cells = package.WorkbookPart!.WorksheetParts.Single().Worksheet.Descendants<S.Cell>().ToArray();
            cells[0].CellFormula = new S.CellFormula("'[missing.xlsx]Sheet1'!A1");
            cells[1].CellFormula = new S.CellFormula("_xlfn.NO_SUCH_FUNCTION(1)");
            cells[7].CellFormula = new S.CellFormula("HYPERLINK(A5,A5)");
        }

        input.Position = 0;
        var sheet = new ExcelReader().Read(input, Cached()).Sheets[0];
        Assert.Equal("0", sheet.Cells[new(1, 1)].Text);
        Assert.Equal("FALSE", sheet.Cells[new(2, 1)].Text);
        Assert.Equal(string.Empty, sheet.Cells[new(8, 1)].Text);
        Assert.Contains(sheet.CellReadDiagnostics, d => d.Code == "FormulaCachedValueMissing" && d.CellRange == "A8");
        Assert.DoesNotContain(sheet.CellReadDiagnostics, d => d.Message.Contains("missing.xlsx") || d.Message.Contains("NO_SUCH"));
    }

    [Theory]
    [InlineData(HyperlinkMode.Preserve)]
    [InlineData(HyperlinkMode.None)]
    public async Task HyperlinkPreservationDoesNotChangeCachedFormulaDisplay(HyperlinkMode hyperlinks)
    {
        using var input = CreateWorkbook();
        using var output = new MemoryStream();
        var result = await ExcelConverter.RenderAsync(input, new()
        {
            OutputFormat = OutputFormat.Markdown,
            ReadOptions = Cached(),
            Hyperlinks = hyperlinks,
        }, new SingleStreamOutputSink(output));
        var text = System.Text.Encoding.UTF8.GetString(output.ToArray());
        // Formula metadata may contain the original friendly argument; cell display remains empty.
        input.Position = 0;
        Assert.Equal(string.Empty, new ExcelReader().Read(input).Sheets[0].Cells[new(8, 1)].Text);
        Assert.Contains("99", text);
        Assert.Contains(result.Diagnostics, d => d.Code == "FormulaCachedValueMissing" && d.CellRange == "A8");
    }

    [Theory]
    [InlineData(ReaderBackend.ClosedXml)]
    [InlineData(ReaderBackend.OpenXml)]
    [InlineData(ReaderBackend.Auto)]
    public void DefaultsKeepStaleCachesAndOriginalInputIncludingHyperlinkLabel(ReaderBackend backend)
    {
        using var input = CreateWorkbook();
        using (var package = SpreadsheetDocument.Open(input, true))
        {
            var cell = package.WorkbookPart!.WorksheetParts.Single().Worksheet.Descendants<S.Cell>().Single(c => c.CellReference == "A8");
            cell.DataType = S.CellValues.String;
            cell.CellValue = new("saved link result");
        }
        input.Position = 0;
        var before = System.Security.Cryptography.SHA256.HashData(input.ToArray());
        var sheet = new ExcelReader().Read(input, new() { Backend = backend }).Sheets[0];
        Assert.Equal("99", sheet.Cells[new(7, 1)].Text);
        Assert.Equal("=SUM(50,2)", sheet.Cells[new(7, 1)].Formula);
        Assert.Equal("saved link result", sheet.Cells[new(8, 1)].Text);
        Assert.Equal(before, System.Security.Cryptography.SHA256.HashData(input.ToArray()));
    }

    [Fact]
    public void UnknownSharedSyntaxDoesNotCauseAutoFallbackOrFormulaExpansion()
    {
        using var input = CreateWorkbook();
        using (var package = SpreadsheetDocument.Open(input, true))
        {
            var cells = package.WorkbookPart!.WorksheetParts.Single().Worksheet.Descendants<S.Cell>().ToArray();
            cells[0].CellFormula = new S.CellFormula("_xlfn.FUTURE(A1#)") { FormulaType = S.CellFormulaValues.Shared, SharedIndex = 9, Reference = "A1:A2" };
            cells[1].CellFormula = new S.CellFormula { FormulaType = S.CellFormulaValues.Shared, SharedIndex = 9 };
        }
        input.Position = 0;
        ReaderBackendSelection? selected = null;
        var sheet = new ExcelReader().Read(input, new() { Backend = ReaderBackend.Auto, BackendSelected = value => selected = value }).Sheets[0];
        Assert.Equal(ReaderBackend.OpenXml, selected!.SelectedBackend);
        Assert.Equal("0", sheet.Cells[new(1, 1)].Text);
        Assert.Equal("FALSE", sheet.Cells[new(2, 1)].Text);
        Assert.Null(sheet.Cells[new(2, 1)].Formula);
        var visual = new Markdown.VisualCellBuilder().Build(sheet).Single(cell => cell.Range.First == new CellAddress(2, 1));
        Assert.Equal(9, visual.FormulaSharedIndex);
        Assert.Equal(new CellAddress(1, 1), visual.FormulaMasterAddress);
        Assert.DoesNotContain(sheet.CellReadDiagnostics, d => d.Code == "FormulaTextUnavailable");
    }

    [Fact]
    public void InvalidPolicyIsRejectedBeforeOpeningInput()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExcelReader().Read("does-not-exist.xlsx", new() { FormulaDisplayMode = (FormulaDisplayMode)100 }));
    }

    [Fact]
    public async Task CachedModeDoesNotReadValueEvenWhenPrecedentWasChangedAfterLoading()
    {
        using var input = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("dirty");
            sheet.Cell("A1").Value = 40;
            sheet.Cell("B1").FormulaA1 = "SUM(A1,2)";
            workbook.SaveAs(input);
        }

        input.Position = 0;
        using (var package = SpreadsheetDocument.Open(input, true))
        {
            package.WorkbookPart!.WorksheetParts.Single().Worksheet.Descendants<S.Cell>().Single(cell => cell.CellReference == "B1").CellValue = new S.CellValue("99");
        }

        input.Position = 0;
        using var prepared = await Core.Input.WorkbookInputPreparer.ReadAsync(input, new(), CancellationToken.None);
        using var original = prepared.OpenRead();
        using var loaded = new XLWorkbook(original);
        loaded.Worksheet(1).Cell("A1").Value = 50;
        loaded.Worksheet(1).Cell("B1").InvalidateFormula();
        var reader = new Core.Excel.CoreExcelReader(new CoreIntegration.FullFontMetrics(null, null, new()));
        var document = reader.ReadClosedXmlWorkbook(prepared, loaded, null, null, CancellationToken.None, Core.Excel.FormulaDisplayMode.CachedValue);
        Assert.Equal("99", document.Sheets[0].Cells[new(1, 2)].Text);
        Assert.True(loaded.Worksheet(1).Cell("B1").NeedsRecalculation);
    }

    private static ExcelReadOptions Cached(ReaderBackend backend = ReaderBackend.ClosedXml) => new() { Backend = backend, FormulaDisplayMode = FormulaDisplayMode.CachedValue };

    private static MemoryStream CreateWorkbook(bool use1904 = false)
    {
        var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            workbook.Use1904DateSystem = use1904;
            var sheet = workbook.AddWorksheet("formulas");
            for (var row = 1; row <= 12; row++)
            {
                sheet.Cell(row, 1).FormulaA1 = "A1+1";
            }

            sheet.Cell(9, 1).Style.NumberFormat.Format = "yyyy-mm-dd";
            sheet.Cell(10, 1).Style.NumberFormat.Format = "0.00%";
            sheet.Cell(11, 1).Style.NumberFormat.Format = "000.00";
            sheet.Cell(12, 1).Style.NumberFormat.Format = "hh:mm";
            workbook.SaveAs(stream);
        }

        stream.Position = 0;
        using (var document = SpreadsheetDocument.Open(stream, true))
        {
            var cells = document.WorkbookPart!.WorksheetParts.Single().Worksheet.Descendants<S.Cell>().ToArray();
            var values = new string?[] { "0", "0", "", null, "saved", "#DIV/0!", "99", null, "2", "0.25", "12.5", "0.5" };
            for (var index = 0; index < cells.Length; index++)
            {
                cells[index].DataType = index switch { 1 => S.CellValues.Boolean, 2 or 4 => S.CellValues.String, 5 => S.CellValues.Error, _ => S.CellValues.Number };
                cells[index].CellValue = values[index] is { } value ? new S.CellValue(value) : null;
            }

            cells[6].CellFormula = new S.CellFormula("SUM(50,2)");
            cells[7].CellFormula = new S.CellFormula("HYPERLINK(\"https://example.invalid\",\"friendly\")");
        }

        stream.Position = 0;
        return stream;
    }
}
