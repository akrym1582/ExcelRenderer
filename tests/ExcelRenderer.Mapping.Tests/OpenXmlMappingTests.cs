using System.Collections;
using System.IO.Compression;
using System.Security.Cryptography;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Xunit;

namespace ExcelRenderer.Mapping.Tests;

public sealed class OpenXmlMappingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void NestedOccurrencesPreserveTypesStylesMergesAndFinalPrintRange(int count)
    {
        using var template = Template(sheet =>
        {
            sheet.Cell("A1").Value = "Header";
            sheet.Cell("A2").Value = "**@start-array groups[*] as group";
            sheet.Cell("A3").Value = "**@group.name";
            sheet.Range("B3:C3").Merge();
            sheet.Cell("B3").Value = "merged";
            sheet.Cell("A4").Value = "**@start-array @group.items[*] as item";
            sheet.Cell("A5").Value = "**@item";
            sheet.Cell("B5").Value = "**@page-break";
            sheet.Row(5).Height = 27;
            sheet.Row(5).OutlineLevel = 1;
            sheet.Cell("Z5").Style.Fill.BackgroundColor = XLColor.Red;
            sheet.Cell("A6").Value = "**@end-array";
            sheet.Cell("A7").Value = "**@end-array";
            sheet.Cell("A8").Value = "Footer";
            sheet.PageSetup.PrintAreas.Add("A1:Z8");
            sheet.PageSetup.SetRowsToRepeatAtTop(1, 1);
        });
        using var output = new MemoryStream();
        MappingBackendSelection? selected = null;
        ExcelTemplateMapper.Map(template, output, new { groups = Enumerable.Range(1, count).Select(index => new { name = "Group " + index, items = new[] { index, index + 10 } }).ToArray() }, new() { Backend = MappingBackend.OpenXml, BackendSelected = value => selected = value });
        Assert.Equal(MappingBackend.OpenXml, selected!.SelectedBackend);
        output.Position = 0;
        using var result = new XLWorkbook(output);
        var sheet = result.Worksheet(1);
        Assert.Equal("Header", sheet.Cell("A1").GetString());
        Assert.Equal("Footer", sheet.Cell(count * 3 + 2, 1).GetString());
        Assert.Equal(count, sheet.MergedRanges.Count());
        for (var index = 0; index < count; index++)
        {
            Assert.Equal("Group " + (index + 1), sheet.Cell(index * 3 + 2, 1).GetString());
            Assert.Equal(index + 1, sheet.Cell(index * 3 + 3, 1).GetDouble());
            Assert.Equal(27, sheet.Row(index * 3 + 3).Height);
            Assert.Equal(XLColor.Red, sheet.Cell(index * 3 + 3, 26).Style.Fill.BackgroundColor);
        }

        Assert.Equal(count * 3 + 2, sheet.PageSetup.PrintAreas.Single().RangeAddress.LastAddress.RowNumber);
        Assert.Equal(1, sheet.PageSetup.FirstRowToRepeatAtTop);
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
        using var template = Template(sheet =>
        {
            sheet.Workbook.Use1904DateSystem = date1904;
            sheet.Cell("A1").Value = "**Date";
            sheet.Cell("A2").Value = "**Duration";
            sheet.Cell("A3").Value = "**Text";
            sheet.Cell("A4").Value = "**Nothing";
            sheet.Cell("A4").Style.Fill.BackgroundColor = XLColor.Red;
        });
        using var output = new MemoryStream();
        var date = new DateTime(2026, 10, 16, 12, 30, 0);
        ExcelTemplateMapper.Map(template, output, new { Date = date, Duration = TimeSpan.FromHours(27.5), Text = "=SUM(A1:A2)", Nothing = (object?)null }, new() { Backend = MappingBackend.OpenXml });
        output.Position = 0;
        using var result = new XLWorkbook(output);
        var sheet = result.Worksheet(1);
        Assert.Equal(date, sheet.Cell("A1").GetDateTime());
        Assert.Equal(TimeSpan.FromHours(27.5), sheet.Cell("A2").GetTimeSpan());
        Assert.Equal("=SUM(A1:A2)", sheet.Cell("A3").GetString());
        Assert.False(sheet.Cell("A3").HasFormula);
        Assert.True(sheet.Cell("A4").IsEmpty());
        Assert.Equal(XLColor.Red, sheet.Cell("A4").Style.Fill.BackgroundColor);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("'literal")]
    [InlineData("=SUM(1,2)")]
    public void ScalarStringsMatchLegacyValueAndQuotePrefix(string value)
    {
        using var template = Template(sheet => sheet.Cell("A1").Value = "**value");
        (string Text, XLDataType Type, bool Quote)? expected = null;
        foreach (var backend in new[] { MappingBackend.ClosedXml, MappingBackend.OpenXml })
        {
            template.Position = 0;
            using var output = new MemoryStream();
            ExcelTemplateMapper.Map(template, output, new { value }, new() { Backend = backend });
            output.Position = 0;
            using var workbook = new XLWorkbook(output);
            var cell = workbook.Worksheet(1).Cell("A1");
            var actual = (cell.GetString(), cell.DataType, cell.Style.IncludeQuotePrefix);
            expected ??= actual;
            Assert.Equal(expected.Value, actual);
            Assert.False(cell.HasFormula);
        }
    }

    [Fact]
    public void InlineStringsUseSpreadsheetEscapesAndRoundTripThroughOpenXmlReader()
    {
        const string value = "_x0041_\u0001";
        using var template = Template(sheet => sheet.Cell("A1").Value = "**value");
        using var output = new MemoryStream();
        ExcelTemplateMapper.Map(template, output, new { value }, new() { Backend = MappingBackend.OpenXml });
        output.Position = 0;
        using (var package = SpreadsheetDocument.Open(output, false))
        {
            Assert.Empty(new OpenXmlValidator().Validate(package));
        }

        output.Position = 0;
        using var reader = ExcelRenderer.OpenXml.OpenXmlWorkbookReader.Open(output);
        Assert.Equal(value, Assert.Single(reader.EnumerateCells(1)).Value.Text);
    }

    [Fact]
    public void UneditedSheetsAndPackagePartsKeepTheirDecompressedBytes()
    {
        using var template = Template(sheet =>
        {
            sheet.Cell("A1").Value = "literal";
            sheet.Workbook.AddWorksheet("other").Cell("B3").Value = "untouched";
        });
        using var output = new MemoryStream();
        ExcelTemplateMapper.Map(template, output, null, new() { Backend = MappingBackend.OpenXml });
        AssertUnchangedParts(template, output, Array.Empty<string>());
    }

    [Fact]
    public void RowMetadataOutsideTheTemplateBodyMovesWithExpansion()
    {
        using var template = Template(sheet =>
        {
            sheet.Cell("A1").Value = "**items[*]";
            sheet.Cell("A2").Value = "Footer";
            sheet.Row(20).Height = 32;
            sheet.Row(20).Hide();
        });
        var outputs = new List<MemoryStream>();
        try
        {
            foreach (var backend in new[] { MappingBackend.ClosedXml, MappingBackend.OpenXml })
            {
                template.Position = 0;
                var output = new MemoryStream();
                outputs.Add(output);
                ExcelTemplateMapper.Map(template, output, new { items = new[] { 1, 2, 3 } }, new() { Backend = backend });
                output.Position = 0;
                using var workbook = new XLWorkbook(output);
                Assert.Equal(32, workbook.Worksheet(1).Row(22).Height);
                Assert.True(workbook.Worksheet(1).Row(22).IsHidden);
            }
        }
        finally
        {
            foreach (var output in outputs) output.Dispose();
        }
    }

    [Fact]
    public void AutoChecksEverySheetWithoutEvaluatingDataTwiceAndExplicitFailurePreservesOutput()
    {
        using var template = Template(sheet =>
        {
            sheet.Cell("A1").Value = "**Items[*]";
            sheet.Workbook.AddWorksheet("formula").Cell("A1").FormulaA1 = "1+1";
        });
        using var output = new MemoryStream();
        output.Write([7, 8, 9]);
        Assert.Throws<NotSupportedException>(() => ExcelTemplateMapper.Map(template, output, new { Items = new[] { 1 } }, new() { Backend = MappingBackend.OpenXml }));
        Assert.Equal(new byte[] { 7, 8, 9 }, output.ToArray());
        template.Position = 0;
        output.SetLength(0);
        var data = new OnceData();
        MappingBackendSelection? selected = null;
        ExcelTemplateMapper.Map(template, output, data, new() { Backend = MappingBackend.Auto, BackendSelected = selection => selected = selection });
        Assert.Equal(MappingBackend.ClosedXml, selected!.SelectedBackend);
        Assert.Contains("FormulasPresent", selected.ReasonCodes);
        Assert.Equal(1, data.ReadCount);
        Assert.Equal(1, data.Values.EnumerationCount);
    }

    [Fact]
    public void AutoUsesOpenXmlForSupportedTemplatesAndInputRemainsOpen()
    {
        using var template = Template(sheet => sheet.Cell("A1").Value = "**Items[*]");
        using var output = new MemoryStream();
        var data = new OnceData();
        MappingBackendSelection? selected = null;
        ExcelTemplateMapper.Map(template, output, data, new() { Backend = MappingBackend.Auto, BackendSelected = selection => selected = selection });
        Assert.Equal(MappingBackend.OpenXml, selected!.SelectedBackend);
        Assert.Equal(1, data.ReadCount);
        Assert.Equal(1, data.Values.EnumerationCount);
        Assert.True(template.CanRead);
        Assert.True(output.CanWrite);
    }

    [Theory]
    [InlineData("table")]
    [InlineData("name")]
    [InlineData("conditional")]
    public void UnsupportedPackageFeaturesAreRejectedBeforeDataAccess(string feature)
    {
        using var template = Template(sheet =>
        {
            sheet.Cell("A1").Value = "**Items[*]";
            if (feature == "table") sheet.Range("A1:A2").CreateTable();
            if (feature == "name") sheet.Range("A1").AddToNamed("custom_name");
            if (feature == "conditional") sheet.Range("A1").AddConditionalFormat().WhenIsBlank().Fill.SetBackgroundColor(XLColor.Red);
        });
        var data = new OnceData();
        using var output = new MemoryStream();
        output.Write([1, 2, 3]);
        Assert.Throws<NotSupportedException>(() => ExcelTemplateMapper.Map(template, output, data, new() { Backend = MappingBackend.OpenXml }));
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
            using (var template = Template(sheet => sheet.Cell("A1").Value = fail ? "**missing" : "**value"))
            using (var file = File.Create(templatePath)) template.CopyTo(file);
            var outputPath = Path.Combine(directory, "output.xlsx");
            File.WriteAllBytes(outputPath, [9, 8, 7]);
            var options = new MappingOptions { Backend = MappingBackend.OpenXml, Input = new() { MemoryThresholdBytes = 1, AllowTemporaryFiles = true, TemporaryDirectory = directory } };
            if (fail)
            {
                Assert.Throws<MappingException>(() => ExcelTemplateMapper.Map(templatePath, outputPath, new { value = "result" }, options));
                Assert.Equal(new byte[] { 9, 8, 7 }, File.ReadAllBytes(outputPath));
            }
            else
            {
                ExcelTemplateMapper.Map(templatePath, outputPath, new { value = "result" }, options);
                using var result = new XLWorkbook(outputPath);
                Assert.Equal("result", result.Worksheet(1).Cell("A1").GetString());
            }

            Assert.Equal(2, Directory.GetFiles(directory).Length);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static MemoryStream Template(Action<IXLWorksheet> configure)
    {
        using var workbook = new XLWorkbook();
        configure(workbook.AddWorksheet("Template"));
        var input = new MemoryStream();
        workbook.SaveAs(input);
        input.Position = 0;
        return input;
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
