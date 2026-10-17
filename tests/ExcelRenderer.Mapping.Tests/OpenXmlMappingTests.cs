using System.Collections;
using System.IO.Compression;
using System.Security.Cryptography;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using ExcelRenderer.Mapping.Configuration;
using ExcelRenderer.Mapping.Diagnostics;
using ExcelRenderer.OpenXml.Model.Cells;
using ExcelRenderer.TestFixtures;
using Xunit;
using static ExcelRenderer.TestFixtures.WorkbookAssertions;
using static ExcelRenderer.Mapping.Tests.MappingFixture;

namespace ExcelRenderer.Mapping.Tests;

public sealed class OpenXmlMappingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void NestedOccurrencesPreserveTypesStylesMergesAndFinalPrintRange(int count)
    {
        using var template = Fixture("OpenXmlMappingTests.NestedOccurrencesPreserveTypesStylesMergesAndFinalPrintRange.CE8E0A6842FA3CE1.xlsx");
        using var output = new MemoryStream();
        ExcelTemplateMapper.Map(template, output, new { groups = Enumerable.Range(1, count).Select(index => new { name = "Group " + index, items = new[] { index, index + 10 } }).ToArray() });
        output.Position = 0;
        using var result = WorkbookProbe.Open(output);
        var sheet = result;
        Assert.Equal("Header", Text(sheet, "A1"));
        Assert.Equal("Footer", Text(sheet, count * 3 + 2, 1));
        Assert.Equal(count, Info(sheet).MergedRanges.Count());
        for (var index = 0; index < count; index++)
        {
            Assert.Equal("Group " + (index + 1), Text(sheet, index * 3 + 2, 1));
            Assert.Equal(index + 1, Number(sheet, index * 3 + 3, 1));
            Assert.Equal(27, Row(sheet, index * 3 + 3).HeightPt);
            Assert.Equal("FFFF0000", Fill(sheet, index * 3 + 3, 26));
        }

        Assert.Equal(count * 3 + 2, Info(sheet).PrintReferences.Single(item => item.Name == "_xlnm.Print_Area").Ranges.Single().LastRow);
        Assert.Equal(1, Info(sheet).PrintReferences.Single(item => item.Name == "_xlnm.Print_Titles").Ranges.Single().FirstRow);
        output.Position = 0;
        using var document = SpreadsheetDocument.Open(output, false);
        Assert.Empty(new OpenXmlValidator().Validate(document));
        AssertUnchangedParts(template, output, new[] { "xl/workbook.xml", "xl/worksheets/sheet1.xml", "xl/sharedStrings.xml" });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TypedValuesDatesDurationsNullAndFormulaLookingStringsAreLiteral(bool date1904)
    {
        using var template = Fixture(date1904 ? "OpenXmlMappingTests.TypedValuesDatesDurationsNullAndFormulaLookingStringsAreLiteral.45F98FAF61363AE0.xlsx" : "OpenXmlMappingTests.TypedValuesDatesDurationsNullAndFormulaLookingStringsAreLiteral.083C24FB574DA086.xlsx");
        using var output = new MemoryStream();
        var date = new DateTime(2026, 10, 16, 12, 30, 0);
        ExcelTemplateMapper.Map(template, output, new { Date = date, Duration = TimeSpan.FromHours(27.5), Text = "=SUM(A1:A2)", Nothing = (object?)null });
        output.Position = 0;
        using var result = WorkbookProbe.Open(output);
        var sheet = result;
        Assert.Equal(date, Date(sheet, "A1"));
        Assert.Equal(TimeSpan.FromHours(27.5), Duration(sheet, "A2"));
        Assert.Equal("=SUM(A1:A2)", Text(sheet, "A3"));
        Assert.False(Cell(sheet, "A3").Formula != null);
        Assert.True(Cell(sheet, "A4").Value.Type == CellValueType.Blank);
        Assert.Equal("FFFF0000", Fill(sheet, "A4"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("'literal")]
    [InlineData("=SUM(1,2)")]
    public void ScalarStringsMatchLegacyValueAndQuotePrefix(string value)
    {
        using var template = Fixture("OpenXmlMappingTests.ScalarStringsMatchLegacyValueAndQuotePrefix.5CB53AA5441CBC44.xlsx");
        var expected = (value.StartsWith("'", StringComparison.Ordinal) ? value[1..] : value, CellValueType.String, value.StartsWith("'", StringComparison.Ordinal));
        template.Position = 0;
        using var output = new MemoryStream();
        ExcelTemplateMapper.Map(template, output, new { value });
        output.Position = 0;
        using var workbook = WorkbookProbe.Open(output);
        var cell = Cell(workbook, "A1");
        var actual = (cell.Value.Text ?? string.Empty, cell.Value.Type, Style(workbook, "A1").Attributes.GetValueOrDefault("quotePrefix") is "1" or "true");
        Assert.Equal(expected, actual);
        Assert.Null(cell.Formula);
    }

    [Fact]
    public void InlineStringsUseSpreadsheetEscapesAndRoundTripThroughOpenXmlReader()
    {
        const string value = "_x0041_\u0001";
        using var template = Fixture("OpenXmlMappingTests.InlineStringsUseSpreadsheetEscapesAndRoundTripThroughOpenXmlReader.5CB53AA5441CBC44.xlsx");
        using var output = new MemoryStream();
        ExcelTemplateMapper.Map(template, output, new { value });
        output.Position = 0;
        using (var package = SpreadsheetDocument.Open(output, false))
        {
            Assert.Empty(new OpenXmlValidator().Validate(package));
        }

        output.Position = 0;
        using var reader = ExcelRenderer.OpenXml.Reading.OpenXmlWorkbookReader.Open(output);
        Assert.Equal(value, Assert.Single(reader.EnumerateCells(1)).Value.Text);
    }

    [Fact]
    public void UneditedSheetsAndPackagePartsKeepTheirDecompressedBytes()
    {
        using var template = Fixture("OpenXmlMappingTests.UneditedSheetsAndPackagePartsKeepTheirDecompressedBytes.9283BC6394252C4C.xlsx");
        using var output = new MemoryStream();
        ExcelTemplateMapper.Map(template, output, null);
        AssertUnchangedParts(template, output, Array.Empty<string>());
    }

    [Fact]
    public void RowMetadataOutsideTheTemplateBodyMovesWithExpansion()
    {
        using var template = Fixture("OpenXmlMappingTests.RowMetadataOutsideTheTemplateBodyMovesWithExpansion.4A91E93E0C468279.xlsx");
        using var output = new MemoryStream();
        ExcelTemplateMapper.Map(template, output, new { items = new[] { 1, 2, 3 } });
        output.Position = 0;
        using var workbook = WorkbookProbe.Open(output);
        Assert.Equal(32, Row(workbook, 22).HeightPt);
        Assert.True(Row(workbook, 22).Hidden == true);
    }

    [Fact]
    public void HiddenFormulasFailBeforeDataOrOutput()
    {
        using var template = Fixture("OpenXmlMappingTests.EveryBackendRejectsHiddenFormulasBeforeSelectionDataOrOutput.65D762ED24172607.xlsx");
        using var output = new MemoryStream();
        output.Write([7, 8, 9]);
        var data = new OnceData();
        var error = Assert.Throws<MappingException>(() => ExcelTemplateMapper.Map(template, output, data));
        Assert.Equal("MappingFormulaNotSupported", error.ReasonCode);
        Assert.Equal("formula", error.SheetName);
        Assert.Equal("A1", error.CellAddress);
        Assert.Equal(0, data.ReadCount);
        Assert.Equal(0, data.Values.EnumerationCount);
        Assert.Equal(new byte[] { 7, 8, 9 }, output.ToArray());
    }

    [Fact]
    public void MappingEvaluatesDataOnceAndLeavesStreamsOpen()
    {
        using var template = Fixture("OpenXmlMappingTests.AutoUsesOpenXmlForSupportedTemplatesAndInputRemainsOpen.A0D4070563D586B5.xlsx");
        using var output = new MemoryStream();
        var data = new OnceData();
        ExcelTemplateMapper.Map(template, output, data);
        Assert.Equal(1, data.ReadCount);
        Assert.Equal(1, data.Values.EnumerationCount);
        Assert.True(template.CanRead);
        Assert.True(output.CanWrite);
    }

    [Theory]
    [InlineData("table")]
    [InlineData("name")]
    [InlineData("extension")]
    public void UnsupportedStructuresAreRejectedBeforeDataAccess(string feature)
    {
        using var template = Fixture(feature switch { "table" => "OpenXmlMappingTests.UnsupportedPackageFeaturesAreRejectedBeforeDataAccess.4F15FC761A9AA40F.xlsx", "extension" => "OpenXmlMappingTests.UnsupportedPackageFeaturesAreRejectedBeforeDataAccess.58F412687F575416.xlsx", "name" => "OpenXmlMappingTests.UnsupportedPackageFeaturesAreRejectedBeforeDataAccess.B592A7ABAC00FAE5.xlsx" , _ => throw new InvalidOperationException() });
        if (feature == "extension")
        {
            using (var package = SpreadsheetDocument.Open(template, true)) package.WorkbookPart!.WorksheetParts.Single().Worksheet.Append(new DocumentFormat.OpenXml.Spreadsheet.ExtensionList());
            template.Position = 0;
        }
        var data = new OnceData();
        using var output = new MemoryStream();
        output.Write([1, 2, 3]);
        if (feature == "extension") Assert.Throws<MappingException>(() => ExcelTemplateMapper.Map(template, output, data));
        else Assert.Throws<MappingException>(() => ExcelTemplateMapper.Map(template, output, data));
        Assert.Equal(0, data.ReadCount);
        Assert.Equal(new byte[] { 1, 2, 3 }, output.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SpilledMappingCleansTemporaryFilesAndAtomicFailuresKeepDestination(bool fail)
    {
        var directory = Path.Combine(Path.GetTempPath(), "mapping-spool-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var templatePath = Path.Combine(directory, "template.xlsx");
            using (var template = Fixture(fail ? "OpenXmlMappingTests.SpilledMappingCleansTemporaryFilesAndAtomicFailuresKeepDestination.2B595009BB2DBE6B.xlsx" : "OpenXmlMappingTests.SpilledMappingCleansTemporaryFilesAndAtomicFailuresKeepDestination.5CB53AA5441CBC44.xlsx"))
            using (var file = File.Create(templatePath)) template.CopyTo(file);
            var outputPath = Path.Combine(directory, "output.xlsx");
            File.WriteAllBytes(outputPath, [9, 8, 7]);
            var options = new MappingOptions { Input = new() { MemoryThresholdBytes = 1, AllowTemporaryFiles = true, TemporaryDirectory = directory } };
            if (fail)
            {
                Assert.Throws<MappingException>(() => ExcelTemplateMapper.Map(templatePath, outputPath, new { value = "result" }, options));
                Assert.Equal(new byte[] { 9, 8, 7 }, File.ReadAllBytes(outputPath));
            }
            else
            {
                ExcelTemplateMapper.Map(templatePath, outputPath, new { value = "result" }, options);
                using var result = WorkbookProbe.Open(outputPath);
                Assert.Equal("result", Text(result, "A1"));
            }

            Assert.Equal(2, Directory.GetFiles(directory).Length);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static void AssertUnchangedParts(Stream input, Stream output, string[] changed)
    {
        input.Position = 0;
        output.Position = 0;
        using var before = new ZipArchive(input, ZipArchiveMode.Read, true);
        using var after = new ZipArchive(output, ZipArchiveMode.Read, true);
        foreach (var entry in before.Entries.Where(entry => !changed.Contains(entry.FullName)))
        {
            using var original = entry.Open();
            using var actual = after.GetEntry(entry.FullName)!.Open();
            Assert.Equal(SHA256.HashData(original), SHA256.HashData(actual));
        }
    }

    public sealed class OnceData
    {
        public int ReadCount { get; private set; }
        public OnceEnumerable Values { get; } = new();
        public OnceEnumerable Items { get { ReadCount++; if (ReadCount != 1) throw new InvalidOperationException("getter repeated"); return Values; } }
    }

    public sealed class OnceEnumerable : IEnumerable
    {
        public int EnumerationCount { get; private set; }
        public IEnumerator GetEnumerator()
        {
            EnumerationCount++;
            if (EnumerationCount != 1) throw new InvalidOperationException("enumeration repeated");
            return new[] { 1, 2, 3 }.GetEnumerator();
        }
    }
}
