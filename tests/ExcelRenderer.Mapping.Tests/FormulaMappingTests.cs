using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.Mapping.Configuration;
using ExcelRenderer.OpenXml.Calculation;
using ExcelRenderer.TestFixtures;
using Xunit;
using S = DocumentFormat.OpenXml.Spreadsheet;
using static ExcelRenderer.TestFixtures.WorkbookAssertions;

namespace ExcelRenderer.Mapping.Tests;

public sealed class FormulaMappingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(70)]
    public void Expanded_invoice_functions_use_the_same_cache_free_mapping_input(int count)
    {
        using var input = Invoice();
        using (var document = SpreadsheetDocument.Open(input, true))
        {
            var workbook = document.WorkbookPart!;
            var cells = workbook.WorksheetParts.First().Worksheet.Descendants<S.Cell>().ToDictionary(c => c.CellReference!.Value!);
            cells["C2"].CellFormula!.Text = "A2*VLOOKUP(B2,Master!$A$1:$B$1,2,FALSE)";
            cells["D3"].CellFormula!.Text = "SUMIFS(Amounts,Amounts,\">0\")";
            cells["F2"].CellFormula!.Text = "TEXT(EOMONTH(DATE(2026,1,1),1),\"yyyy-mm-dd\")";
            SdkWorkbook.AddSheet(workbook, new S.Worksheet(new S.SheetData(new S.Row(SdkWorkbook.Number("A1", "10"), SdkWorkbook.Number("B1", "10")) { RowIndex = 1 })), "Master");
        }
        input.Position = 0;
        using var mapped = new MemoryStream();
        ExcelTemplateMapper.Map(input, mapped, new { items = Enumerable.Range(1, count).Select(quantity => new { quantity, price = 10 }).ToArray() }, new MappingOptions { FormulaCalculationMode = FormulaCalculationMode.Recalculate, RecalculateMappedWorkbook = false });
        using (var book = WorkbookProbe.Open(mapped))
        {
            foreach (var sheet in book.Reader.SelectedWorksheetIndices)
                Assert.All(book.Reader.EnumerateCells(sheet).Where(c => c.Formula != null), cell => Assert.False(cell.Formula!.HasCachedResult));
            if (count > 0) Assert.Equal($"(A{count + 1}*VLOOKUP(B{count + 1},'Master'!$A$1:$B$1,2,FALSE))", Cell(book, count + 1, 3).Formula!.RawFormula);
        }
        mapped.Position = 0;
        using var output = new MemoryStream();
        WorkbookRecalculator.Recalculate(mapped, output);
        using var calculated = WorkbookProbe.Open(output);
        Assert.Equal(count * (count + 1) / 2 * 11, Number(calculated, count + 2, 4));
        for (int row = 2; row <= count + 1; row++) Assert.Equal("2026-02-28", Text(calculated, row, 6));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    public void Invoice_expands_references_and_recalculates_saved_caches(int count)
    {
        using var input = Invoice();
        using var output = new MemoryStream();
        ExcelTemplateMapper.Map(input, output, new { items = Enumerable.Range(1, count).Select(i => new { quantity = i, price = 10 }).ToArray() }, new MappingOptions { FormulaCalculationMode = FormulaCalculationMode.Recalculate });
        using var book = WorkbookProbe.Open(output);
        for (var i = 1; i <= count; i++)
        {
            Assert.Equal(i * 10, Number(book, i + 1, 3));
            Assert.Equal(i * 11, Number(book, i + 1, 4));
            Assert.Equal(i + 10 + 0.1 + 7, Number(book, i + 1, 5), 10);
            Assert.Equal("A2:$A$2", Text(book, i + 1, 6));
        }
        var expected = count * (count + 1) / 2 * 11;
        Assert.Equal(expected, Number(book, count + 2, 4));
        Assert.Equal(expected, Number(book, "A2", 2));
        Assert.Equal(7, Number(book, count + 3, 1));
        if (count > 0) Assert.Contains("$A$" + (count + 3), Cell(book, "E2").Formula!.RawFormula);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Multirow_blocks_keep_relative_references_in_the_same_occurrence(int count)
    {
        using var input = SdkWorkbook.Create(new S.Worksheet(new S.SheetData(
            new S.Row(SdkWorkbook.Number("B1", "10")) { RowIndex = 1 },
            new S.Row(SdkWorkbook.Text("A2", "**@start-array items[*] as item")) { RowIndex = 2 },
            new S.Row(SdkWorkbook.Text("A3", "**@item")) { RowIndex = 3 },
            new S.Row(Formula("C4", "A3*$B$1")) { RowIndex = 4 },
            new S.Row(SdkWorkbook.Text("A5", "**@end-array")) { RowIndex = 5 },
            new S.Row(Formula("C6", "SUM(C1:C4)")) { RowIndex = 6 })), SdkWorkbook.Styles());
        using var output = new MemoryStream();
        ExcelTemplateMapper.Map(input, output, new { items = Enumerable.Range(1, count).ToArray() }, new MappingOptions { FormulaCalculationMode = FormulaCalculationMode.Recalculate });
        using var book = WorkbookProbe.Open(output);
        for (var i = 1; i <= count; i++)
            Assert.Equal(i * 10, Number(book, 1 + i * 2, 3));
        Assert.Equal(count * (count + 1) / 2 * 10, Number(book, 2 + count * 2, 3));
    }

    [Fact]
    public void Shared_formulas_are_restored_before_expanding_rows()
    {
        using var input = Invoice();
        using (var document = SpreadsheetDocument.Open(input, true))
        {
            var cells = document.WorkbookPart!.WorksheetParts.First().Worksheet.Descendants<S.Cell>().ToDictionary(c => c.CellReference!.Value!);
            cells["C2"].CellFormula = new S.CellFormula("A2*B2") { FormulaType = S.CellFormulaValues.Shared, SharedIndex = 0, Reference = "C2:D2" };
            cells["D2"].CellFormula = new S.CellFormula { FormulaType = S.CellFormulaValues.Shared, SharedIndex = 0 };
        }
        input.Position = 0;
        using var output = new MemoryStream();
        ExcelTemplateMapper.Map(input, output, new { items = Enumerable.Range(2, 3).Select(quantity => new { quantity, price = 10 }).ToArray() }, new MappingOptions { FormulaCalculationMode = FormulaCalculationMode.Recalculate });
        using var book = WorkbookProbe.Open(output);
        for (var row = 2; row <= 4; row++)
        {
            Assert.Equal(row * 10, Number(book, row, 3));
            Assert.Equal(row * 100, Number(book, row, 4));
            Assert.Equal("normal", Cell(book, row, 4).Formula!.Kind);
        }
    }

    [Fact]
    public void Rewriting_without_evaluation_invalidates_every_saved_formula_cache()
    {
        using var input = Invoice();
        using var output = new MemoryStream();
        ExcelTemplateMapper.Map(input, output, new { items = new[] { new { quantity = 2, price = 10 } } }, new MappingOptions { FormulaCalculationMode = FormulaCalculationMode.Recalculate, RecalculateMappedWorkbook = false });
        using var book = WorkbookProbe.Open(output);
        foreach (var sheet in book.Reader.SelectedWorksheetIndices)
            Assert.All(book.Reader.EnumerateCells(sheet).Where(c => c.Formula != null), cell => Assert.False(cell.Formula!.HasCachedResult));
        Assert.True(book.Document.WorkbookPart!.Workbook.CalculationProperties!.FullCalculationOnLoad!.Value);
    }

    [Fact]
    public void Metadata_backed_formula_is_rejected_before_rewriting()
    {
        using var input = Invoice();
        using (var document = SpreadsheetDocument.Open(input, true))
            document.WorkbookPart!.WorksheetParts.First().Worksheet.Descendants<S.Cell>().Single(c => c.CellReference == "C2").SetAttribute(new DocumentFormat.OpenXml.OpenXmlAttribute("cm", string.Empty, "1"));
        input.Position = 0;
        using var output = new MemoryStream();
        var error = Assert.Throws<NotSupportedException>(() => ExcelTemplateMapper.Map(input, output, new { items = new[] { new { quantity = 2, price = 10 } } }, new MappingOptions { FormulaCalculationMode = FormulaCalculationMode.Recalculate, RecalculateMappedWorkbook = false }));
        Assert.Contains("calculation metadata", error.Message);
        Assert.Contains("Invoice!C2", error.Message);
        Assert.Empty(output.ToArray());
    }

    [Theory]
    [InlineData("UNKNOWN(A2)")]
    [InlineData("C2")]
    [InlineData("Table1[Amount]")]
    public void Failed_recalculation_does_not_modify_destination(string formula)
    {
        using var input = Invoice();
        using (var document = SpreadsheetDocument.Open(input, true))
            document.WorkbookPart!.WorksheetParts.First().Worksheet.Descendants<S.Cell>().Single(c => c.CellReference == "C2").CellFormula!.Text = formula;
        input.Position = 0;
        using var output = new MemoryStream();
        output.Write(new byte[] { 1, 2, 3 });
        Assert.ThrowsAny<Exception>(() => ExcelTemplateMapper.Map(input, output, new { items = new[] { new { quantity = 2, price = 10 } } }, new MappingOptions { FormulaCalculationMode = FormulaCalculationMode.Recalculate }));
        Assert.Equal(new byte[] { 1, 2, 3 }, output.ToArray());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void String_limit_failure_during_mapping_preserves_destination(bool defaults, bool cacheBudget)
    {
        using var input = Invoice();
        using (var document = SpreadsheetDocument.Open(input, true))
            document.WorkbookPart!.WorksheetParts.First().Worksheet.Descendants<S.Cell>().Single(c => c.CellReference == "C2").CellFormula!.Text = "A2&A2";
        input.Position = 0;
        using var output = new MemoryStream();
        output.Write(new byte[] { 1, 2, 3 });
        var options = new MappingOptions
        {
            FormulaCalculationMode = FormulaCalculationMode.Recalculate,
            FormulaEvaluationOptions = defaults ? new() : new() { MaxStringLength = cacheBudget ? 100 : 8, MaxCachedStringCharacters = cacheBudget ? 8 : 100 },
        };
        string text = new('x', defaults ? 16384 : 5);
        var exception = Assert.Throws<FormulaCalculationException>(() => ExcelTemplateMapper.Map(input, output, new { items = new[] { new { quantity = text, price = 10 } } }, options));
        Assert.Equal("ResourceLimitExceeded", Assert.Single(exception.Diagnostics).Code);
        Assert.Equal(new byte[] { 1, 2, 3 }, output.ToArray());
    }

    private static MemoryStream Invoice()
    {
        var input = SdkWorkbook.Create(new S.Worksheet(new S.SheetData(
            new S.Row(SdkWorkbook.Text("A1", "Invoice")) { RowIndex = 1 },
            new S.Row(SdkWorkbook.Text("A2", "**items[*].quantity"), SdkWorkbook.Text("B2", "**items[*].price"), Formula("C2", "A2*B2"), Formula("D2", "ROUND(C2*(1+Rates!$A$1),0)"), Formula("E2", "$A2+B$2+Rates!$A$1+$A$4"), Formula("F2", "\"A2:$A$2\"")) { RowIndex = 2 },
            new S.Row(Formula("D3", "SUM(Amounts)")) { RowIndex = 3 },
            new S.Row(SdkWorkbook.Number("A4", "7")) { RowIndex = 4 })), SdkWorkbook.Styles(), name: "Invoice");
        using (var document = SpreadsheetDocument.Open(input, true))
        {
            SdkWorkbook.AddSheet(document.WorkbookPart!, new S.Worksheet(new S.SheetData(
                new S.Row(SdkWorkbook.Number("A1", "0.1")) { RowIndex = 1 },
                new S.Row(Formula("A2", "Invoice!$D$3")) { RowIndex = 2 })), "Rates");
            document.WorkbookPart!.Workbook.DefinedNames = new S.DefinedNames(new S.DefinedName("Invoice!$D$1:$D$2") { Name = "Amounts" });
        }
        input.Position = 0;
        return input;
    }

    private static S.Cell Formula(string address, string text) => new(new S.CellFormula(text), new S.CellValue("999")) { CellReference = address };
}
