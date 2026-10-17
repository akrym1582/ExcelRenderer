using System.Collections;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.TestFixtures;
using Xunit;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.Mapping.Tests;

public sealed class FormulaPreflightTests
{
    [Theory]
    [InlineData("normal")]
    [InlineData("shared-master")]
    [InlineData("shared-follower")]
    [InlineData("array")]
    [InlineData("dataTable")]
    [InlineData("dynamic")]
    [InlineData("table-calculated")]
    [InlineData("table-total")]
    [InlineData("table-total-function")]
    [InlineData("name")]
    [InlineData("extension")]
    public void AllValueFormulaKindsFailBeforeGetterEnumerationAndSink(string kind)
    {
        using var input = Template();
        using (var package = SpreadsheetDocument.Open(input, true))
        {
            var workbook = package.WorkbookPart!;
            var sheet = workbook.WorksheetParts.Single();
            var cell = sheet.Worksheet.Descendants<S.Cell>().Single(c => c.CellReference == "B2");
            if (kind == "name") workbook.Workbook.DefinedNames = new S.DefinedNames(new S.DefinedName("SUM(1,2)") { Name = "Computed" });
            else if (kind.StartsWith("table-", StringComparison.Ordinal))
            {
                var part = sheet.AddNewPart<TableDefinitionPart>();
                using var text = new MemoryStream(Encoding.UTF8.GetBytes($"<table xmlns='{Ns}' name='Amounts' displayName='Amounts'><tableColumns><tableColumn id='1' name='Amount'><{(kind == "table-total" ? "totalsRowFormula" : "calculatedColumnFormula")}>1+1</{(kind == "table-total" ? "totalsRowFormula" : "calculatedColumnFormula")}></tableColumn></tableColumns></table>"));
                if (kind == "table-total-function")
                {
                    using var totals = new MemoryStream(Encoding.UTF8.GetBytes($"<table xmlns='{Ns}' name='Amounts' displayName='Amounts'><tableColumns><tableColumn id='1' name='Amount' totalsRowFunction='sum'/></tableColumns></table>"));
                    part.FeedData(totals);
                }
                else
                {
                    part.FeedData(text);
                }
            }
            else if (kind == "extension") sheet.Worksheet.Append(new S.ExtensionList());
            else
            {
                cell.CellFormula = kind switch
                {
                    "shared-master" => new S.CellFormula("1+1") { FormulaType = S.CellFormulaValues.Shared, SharedIndex = 7, Reference = "B2:B3" },
                    "shared-follower" => new S.CellFormula { FormulaType = S.CellFormulaValues.Shared, SharedIndex = 7 },
                    "array" or "dynamic" => new S.CellFormula("_xlfn.SEQUENCE(2)") { FormulaType = S.CellFormulaValues.Array, Reference = "B2:B3" },
                    "dataTable" => new S.CellFormula { FormulaType = S.CellFormulaValues.DataTable, Reference = "B2:B3" },
                    _ => new S.CellFormula("1+1"),
                };
            }
        }
        input.Position = 0;
        using var output = new MemoryStream();
        output.Write([2, 7, 1]);
        var data = new ThrowingData();
        var error = Assert.Throws<MappingException>(() => ExcelTemplateMapper.Map(input, output, data));
        Assert.Equal("MappingFormulaNotSupported", error.ReasonCode);
        Assert.Contains("replace template formulas with value placeholders", error.Message);
        Assert.Equal(kind == "name" ? "Computed" : kind.StartsWith("table-", StringComparison.Ordinal) ? "Amounts" : string.Empty, error.Expression);
        Assert.Equal(0, data.Reads);
        Assert.Equal(new byte[] { 2, 7, 1 }, output.ToArray());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("manual")]
    [InlineData("auto")]
    public void SavingPreservesOriginalCalculationPropertiesWithoutAddingRequests(string? mode)
    {
        using var input = Template();
        string? expected;
        using (var package = SpreadsheetDocument.Open(input, true))
        {
            package.WorkbookPart!.Workbook.CalculationProperties = mode is null ? null : new S.CalculationProperties
            {
                CalculationMode = mode == "manual" ? S.CalculateModeValues.Manual : S.CalculateModeValues.Auto,
                FullCalculationOnLoad = false,
                ForceFullCalculation = false,
            };
            expected = package.WorkbookPart.Workbook.CalculationProperties?.OuterXml;
        }
        input.Position = 0;
        using var output = new MemoryStream();
        ExcelTemplateMapper.Map(input, output, new { Items = new[] { "=SUM(1,2)" } });
        output.Position = 0;
        using var actual = SpreadsheetDocument.Open(output, false);
        Assert.Equal(expected, actual.WorkbookPart!.Workbook.CalculationProperties?.OuterXml);
        Assert.Empty(actual.WorkbookPart.WorksheetParts.Single().Worksheet.Descendants<S.CellFormula>());
    }

    [Fact]
    public void ConditionalAndValidationFormulasRemainStructuralCompatibilityMetadata()
    {
        using var input = Template();
        using (var package = SpreadsheetDocument.Open(input, true))
        {
            var sheet = package.WorkbookPart!.WorksheetParts.Single().Worksheet;
            sheet.Append(new S.ConditionalFormatting(new S.ConditionalFormattingRule(new S.Formula("A1>0"))
            {
                Type = S.ConditionalFormatValues.Expression, Priority = 1,
            }) { SequenceOfReferences = new DocumentFormat.OpenXml.ListValue<DocumentFormat.OpenXml.StringValue> { InnerText = "A1" } });
            sheet.Append(new S.DataValidations(new S.DataValidation(new S.Formula1("A1>0"))
            {
                Type = S.DataValidationValues.Custom,
                SequenceOfReferences = new DocumentFormat.OpenXml.ListValue<DocumentFormat.OpenXml.StringValue> { InnerText = "A1" },
            }));
        }
        input.Position = 0;
        using var output = new MemoryStream();
        ExcelTemplateMapper.Map(input, output, new { Items = new[] { 42 } });
        output.Position = 0;
        using var actual = SpreadsheetDocument.Open(output, false);
        var result = actual.WorkbookPart!.WorksheetParts.Single().Worksheet;
        Assert.Empty(result.Descendants<S.CellFormula>());
        Assert.Equal("A1>0", Assert.Single(result.Descendants<S.Formula>()).Text);
        Assert.Equal("A1>0", Assert.Single(result.Descendants<S.Formula1>()).Text);
    }

    [Fact]
    public void FileFormulaRejectionPreservesExistingDestination()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(directory, "template.xlsx");
            var destination = Path.Combine(directory, "output.xlsx");
            using (var input = Template())
            {
                using (var package = SpreadsheetDocument.Open(input, true)) package.WorkbookPart!.WorksheetParts.Single().Worksheet.Descendants<S.Cell>().First().CellFormula = new S.CellFormula("1+1");
                File.WriteAllBytes(source, input.ToArray());
            }
            File.WriteAllBytes(destination, [5, 6, 7]);
            Assert.Throws<MappingException>(() => ExcelTemplateMapper.Map(source, destination, new ThrowingData()));
            Assert.Equal(new byte[] { 5, 6, 7 }, File.ReadAllBytes(destination));
            Assert.Empty(Directory.GetFiles(directory, ".excelrenderer-*.tmp"));
        }
        finally { Directory.Delete(directory, true); }
    }

    private const string Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static MemoryStream Template()
    {
        using var source = RetainedWorkbook.Open("FormulaPreflightTests.AllValueFormulaKindsFailBeforeBackendGetterEnumerationAndSink.EECE108D361084C8.xlsx");
        var stream = new MemoryStream();
        source.CopyTo(stream);
        stream.Position = 0;
        return stream;
    }

    private sealed class ThrowingData
    {
        public int Reads { get; private set; }
        public IEnumerable Items { get { Reads++; throw new InvalidOperationException("getter must not run"); } }
    }
}
