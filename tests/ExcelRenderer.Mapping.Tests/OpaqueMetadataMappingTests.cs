using System.IO.Compression;
using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.Mapping.Configuration;
using ExcelRenderer.Mapping.Diagnostics;
using ExcelRenderer.OpenXml.Calculation;
using ExcelRenderer.TestFixtures;
using Xunit;
using S = DocumentFormat.OpenXml.Spreadsheet;
using C = DocumentFormat.OpenXml.Drawing.Charts;
using static ExcelRenderer.TestFixtures.WorkbookAssertions;

namespace ExcelRenderer.Mapping.Tests;

public sealed class OpaqueMetadataMappingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Fixed_cells_preserve_opaque_parts_and_extensions(bool recalculate)
    {
        using var input = Template(formula: recalculate);
        var originalParts = Parts(input);
        using var output = new MemoryStream();
        ExcelTemplateMapper.Map(input, output, new { value = "mapped" }, new MappingOptions
        {
            FormulaCalculationMode = recalculate ? FormulaCalculationMode.Recalculate : FormulaCalculationMode.CachedValue,
        });
        var mappedParts = Parts(output);
        foreach (var part in originalParts.Where(part => part.Key.Contains("/charts/", StringComparison.Ordinal) || part.Key.Contains("printerSettings", StringComparison.Ordinal)))
        {
            Assert.Equal(part.Value, mappedParts[part.Key]);
        }

        using var book = WorkbookProbe.Open(output);
        Assert.Equal("mapped", Text(book, "A1"));
        Assert.Equal("keep-workbook", book.Document.WorkbookPart!.Workbook.GetFirstChild<S.WorkbookExtensionList>()!.InnerText);
        Assert.Equal("keep-sheet", Xml(book).ChildElements.Single(child => child.LocalName == "extLst").InnerText);
        Assert.Equal("keep-styles", book.Document.WorkbookPart.WorkbookStylesPart!.Stylesheet.ChildElements.Single(child => child.LocalName == "extLst").InnerText);
        Assert.NotNull(book.Document.WorkbookPart.WorksheetParts.Single().SpreadsheetPrinterSettingsParts.Single());
        if (recalculate)
        {
            Assert.Equal(6, Number(book, "B1"));
        }
    }

    [Fact]
    public void Opaque_metadata_does_not_hide_cell_formulas_in_cached_mode()
    {
        using var input = Template(formula: true);
        using var output = new MemoryStream();
        output.Write([7, 8]);
        var data = new ThrowingData();
        var error = Assert.Throws<MappingException>(() => ExcelTemplateMapper.Map(input, output, data));
        Assert.Equal("MappingFormulaNotSupported", error.ReasonCode);
        Assert.Equal("B1", error.CellAddress);
        Assert.Equal(new byte[] { 7, 8 }, output.ToArray());
    }

    [Fact]
    public void Repeated_rows_still_reject_opaque_metadata_before_evaluating_data()
    {
        using var input = Template(repeated: true);
        using var output = new MemoryStream();
        output.Write([7, 8]);
        Assert.Throws<NotSupportedException>(() => ExcelTemplateMapper.Map(input, output, new ThrowingData(), new MappingOptions
        {
            FormulaCalculationMode = FormulaCalculationMode.Recalculate,
        }));
        Assert.Equal(new byte[] { 7, 8 }, output.ToArray());
    }

    private static MemoryStream Template(bool formula = false, bool repeated = false)
    {
        var row = new S.Row(SdkWorkbook.Text("A1", repeated ? "**value[*]" : "**value")) { RowIndex = 1 };
        if (formula) row.Append(new S.Cell(new S.CellFormula("LEN(A1)"), new S.CellValue("999")) { CellReference = "B1" });
        var input = SdkWorkbook.Create(new S.Worksheet(new S.SheetData(row)), SdkWorkbook.Styles());
        using (var package = SpreadsheetDocument.Open(input, true))
        {
            var workbook = package.WorkbookPart!;
            workbook.Workbook.Append(new S.WorkbookExtensionList(new S.WorkbookExtension { Uri = "opaque-workbook", InnerXml = "<payload xmlns='urn:test'>keep-workbook</payload>" }));
            workbook.WorkbookStylesPart!.Stylesheet.Append(new S.ExtensionList(new S.Extension { Uri = "opaque-styles", InnerXml = "<payload xmlns='urn:test'>keep-styles</payload>" }));
            var sheet = workbook.WorksheetParts.Single();
            var printer = sheet.AddNewPart<SpreadsheetPrinterSettingsPart>();
            using (var bytes = new MemoryStream(new byte[] { 1, 2, 3, 4 })) printer.FeedData(bytes);
            sheet.Worksheet.Append(new S.PageSetup { Id = sheet.GetIdOfPart(printer) });
            sheet.Worksheet.Append(new S.ExtensionList(new S.Extension { Uri = "opaque-sheet", InnerXml = "<payload xmlns='urn:test'>keep-sheet</payload>" }));
            var drawing = sheet.AddNewPart<DrawingsPart>();
            drawing.WorksheetDrawing = new DocumentFormat.OpenXml.Drawing.Spreadsheet.WorksheetDrawing();
            var chart = drawing.AddNewPart<ChartPart>();
            chart.ChartSpace = new C.ChartSpace(new C.Chart(new C.PlotArea()));
        }

        input.Position = 0;
        return input;
    }

    private static Dictionary<string, byte[]> Parts(Stream source)
    {
        source.Position = 0;
        using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        var result = new Dictionary<string, byte[]>();
        foreach (var entry in archive.Entries)
        {
            using var stream = entry.Open();
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            result.Add(entry.FullName, copy.ToArray());
        }

        source.Position = 0;
        return result;
    }

    private sealed class ThrowingData
    {
        public string value => throw new InvalidOperationException("Mapping data must not be evaluated.");
    }
}
