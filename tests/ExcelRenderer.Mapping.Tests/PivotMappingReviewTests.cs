using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using ExcelRenderer.OpenXml.Writing;
using ExcelRenderer.OpenXml.Writing.Plans;
using ExcelRenderer.TestFixtures;
using Xunit;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.Mapping.Tests;

public sealed class PivotMappingReviewTests
{
    private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void SourceMovementKeepsEveryItemSelectionAxisAndFilter(int count)
    {
        using var input = Fixture();
        var before = PivotParts(input);
        using var output = new MemoryStream();
        ExcelTemplateMapper.Map(input, output, new { items = Enumerable.Range(1, count).ToArray() });
        output.Position = 0;
        using var actual = SpreadsheetDocument.Open(output, false);
        Assert.Empty(new OpenXmlValidator().Validate(actual).Select(error => error.Description));
        var cache = actual.WorkbookPart!.PivotTableCacheDefinitionParts.Single();
        Assert.Equal($"A{count + 3}:D{count + 5}", cache.PivotCacheDefinition.CacheSource!.WorksheetSource!.Reference!.Value);
        Assert.Equal(before.Single(pair => pair.Key.Contains("Records")).Value, PivotParts(output).Single(pair => pair.Key.Contains("Records")).Value);
        foreach (var pivot in actual.WorkbookPart.WorksheetParts.Single().PivotTableParts)
        {
            AssertSettings(pivot, cache);
            Assert.EndsWith((count + 3).ToString(CultureInfo.InvariantCulture), pivot.PivotTableDefinition.Location!.Reference!.Value);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DataChangesAppendTypedIdsAndKeepMissingItemsAndAllPivotPositions(bool reverse)
    {
        using var input = Fixture();
        var values = reverse ? new object[] { "New", 1d } : new object[] { 1d, "New" };
        var rows = Enumerable.Range(1, 6).Select(row => new OutputRowOccurrence(row, row, 0, 0,
            row >= 5 ? new[] { values[row - 5] is string text ? new CellValueEdit(1, text) : new CellValueEdit(1, (double)values[row - 5]) } : Array.Empty<CellValueEdit>())).ToArray();
        using var output = new MemoryStream();
        OpenXmlWorkbookWriter.Write(input, output, new WorkbookEditPlan([new SheetEditPlan(1, 6, rows, [], [], false)]));
        output.Position = 0;
        using var actual = SpreadsheetDocument.Open(output, false);
        Assert.Empty(new OpenXmlValidator().Validate(actual).Select(error => error.Description));
        var cache = actual.WorkbookPart!.PivotTableCacheDefinitionParts.Single();
        using var source = cache.GetStream();
        var xml = XDocument.Load(source);
        var items = xml.Descendants(Ns + "cacheField").First().Element(Ns + "sharedItems")!.Elements().ToArray();
        Assert.Equal(new[] { "s:1", "s:Two", reverse ? "s:New" : "n:1", reverse ? "n:1" : "s:New" }, items.Select(value => value.Name.LocalName + ":" + (string?)value.Attribute("v")));
        Assert.True(cache.PivotCacheDefinition.RefreshOnLoad!.Value);
        Assert.True(cache.PivotCacheDefinition.Invalid!.Value);
        using var recordsInput = cache.PivotTableCacheRecordsPart!.GetStream();
        var records = XDocument.Load(recordsInput).Root!.Elements().ToArray();
        Assert.Equal(reverse ? "s:New" : "n:1", ResolveRecord(records[0], items));
        Assert.Equal(reverse ? "n:1" : "s:New", ResolveRecord(records[1], items));
        foreach (var pivot in actual.WorkbookPart.WorksheetParts.Single().PivotTableParts)
        {
            AssertSettings(pivot, cache);
            var field = XElement.Parse(pivot.PivotTableDefinition.OuterXml).Element(Ns + "pivotFields")!.Elements().First();
            Assert.Equal(new[] { "1", "0", null, "2", "3" }, field.Element(Ns + "items")!.Elements().Select(item => (string?)item.Attribute("x")));
        }
    }

    [Fact]
    public void EquivalentNumericValuesReuseTheExistingSharedIdAndItsMetadata()
    {
        using var input = Fixture();
        using (var package = SpreadsheetDocument.Open(input, true))
        {
            var cache = package.WorkbookPart!.PivotTableCacheDefinitionParts.Single();
            var shared = cache.PivotCacheDefinition.CacheFields!.First().GetFirstChild<S.SharedItems>()!;
            shared.FirstChild!.Remove();
            shared.PrependChild(new S.NumberItem { Val = 1, Caption = "Saved caption" });
        }

        input.Position = 0;
        var rows = Enumerable.Range(1, 6).Select(row => new OutputRowOccurrence(row, row, 0, 0,
            row == 5 ? new[] { new CellValueEdit(1, 1d) } : Array.Empty<CellValueEdit>())).ToArray();
        using var output = new MemoryStream();
        OpenXmlWorkbookWriter.Write(input, output, new WorkbookEditPlan([new SheetEditPlan(1, 6, rows, [], [], false)]));
        output.Position = 0;
        using var actual = SpreadsheetDocument.Open(output, false);
        var saved = actual.WorkbookPart!.PivotTableCacheDefinitionParts.Single().PivotCacheDefinition.CacheFields!.First().GetFirstChild<S.SharedItems>()!;
        Assert.Equal(2, saved.ChildElements.Count);
        Assert.Equal("Saved caption", saved.GetFirstChild<S.NumberItem>()!.Caption!.Value);
        Assert.Empty(new OpenXmlValidator().Validate(actual).Select(error => error.Description));
    }

    [Fact]
    public void ImplicitBaseFieldKeepsSharedIdsEvenWithoutPivotItems()
    {
        using var input = Fixture();
        using (var package = SpreadsheetDocument.Open(input, true))
        {
            foreach (var pivot in package.WorkbookPart!.WorksheetParts.Single().PivotTableParts)
            {
                var root = pivot.PivotTableDefinition;
                var field = root.PivotFields!.Elements<S.PivotField>().First();
                field.RemoveAllChildren();
                field.Axis = null;
                root.RowFields!.Remove();
                root.RowItems!.Remove();
                var data = root.DataFields!.Elements<S.DataField>().Single();
                data.BaseField = null;
                data.ShowDataAs = S.ShowDataAsValues.Difference;
            }
        }
        input.Position = 0;
        var rows = Enumerable.Range(1, 6).Select(row => new OutputRowOccurrence(row, row, 0, 0,
            row >= 5 ? new[] { new CellValueEdit(1, row == 5 ? "New" : "1") } : Array.Empty<CellValueEdit>())).ToArray();
        using var output = new MemoryStream();
        OpenXmlWorkbookWriter.Write(input, output, new WorkbookEditPlan([new SheetEditPlan(1, 6, rows, [], [], false)]));
        output.Position = 0;
        using var actual = SpreadsheetDocument.Open(output, false);
        var shared = actual.WorkbookPart!.PivotTableCacheDefinitionParts.Single().PivotCacheDefinition.CacheFields!.First().GetFirstChild<S.SharedItems>()!;
        Assert.Equal(new[] { "1", "Two", "New" }, shared.Elements<S.StringItem>().Select(value => value.Val!.Value));
        Assert.All(actual.WorkbookPart.WorksheetParts.Single().PivotTableParts, pivot =>
        {
            var data = pivot.PivotTableDefinition.DataFields!.Elements<S.DataField>().Single();
            Assert.Null(data.BaseField);
            Assert.Equal(1u, data.BaseItem!.Value);
        });
        Assert.Empty(new OpenXmlValidator().Validate(actual).Select(error => error.Description));
    }

    [Fact]
    public void IdentityEditPreservesCleanPivotPartsByteForByte()
    {
        using var input = Fixture();
        var before = PivotParts(input);
        using var output = new MemoryStream();
        var rows = Enumerable.Range(1, 6).Select(row => new OutputRowOccurrence(row, row, 0, 0, Array.Empty<CellValueEdit>())).ToArray();
        OpenXmlWorkbookWriter.Write(input, output, new WorkbookEditPlan([new SheetEditPlan(1, 6, rows, [], [], false)]));
        var after = PivotParts(output);
        foreach (var pair in before) Assert.Equal(pair.Value, after[pair.Key]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Fixed_source_mapping_updates_pivots_while_preserving_printer_settings(bool recalculate)
    {
        using var input = Fixture();
        byte[] printerBytes = [1, 2, 3, 4];
        using (var package = SpreadsheetDocument.Open(input, true))
        {
            var sheet = package.WorkbookPart!.WorksheetParts.Single();
            var printer = sheet.AddNewPart<SpreadsheetPrinterSettingsPart>();
            using var bytes = new MemoryStream(printerBytes);
            printer.FeedData(bytes);
            sheet.Worksheet.Append(new S.PageSetup { Id = sheet.GetIdOfPart(printer), Scale = 75 });
            var data = sheet.Worksheet.GetFirstChild<S.SheetData>()!;
            data.Elements<S.Row>().Single(row => row.RowIndex?.Value == 2).Remove();
            var cell = data.Descendants<S.Cell>().Single(cell => cell.CellReference == "A5");
            cell.Parent!.ReplaceChild(SdkWorkbook.Text("A5", "**category"), cell);
        }

        input.Position = 0;
        using var output = new MemoryStream();
        var evidenceDirectory = Path.Combine(AppContext.BaseDirectory, "SampleOutputs", "PivotOpaque");
        Directory.CreateDirectory(evidenceDirectory);
        File.WriteAllBytes(Path.Combine(evidenceDirectory, "template.xlsx"), input.ToArray());
        ExcelTemplateMapper.Map(input, output, new { category = "New" }, new Configuration.MappingOptions
        {
            FormulaCalculationMode = recalculate ? OpenXml.Calculation.FormulaCalculationMode.Recalculate : OpenXml.Calculation.FormulaCalculationMode.CachedValue,
        });
        output.Position = 0;
        using var actual = SpreadsheetDocument.Open(output, false);
        Assert.Empty(new OpenXmlValidator().Validate(actual).Select(error => error.Description));
        var worksheet = actual.WorkbookPart!.WorksheetParts.Single();
        Assert.Equal("New", worksheet.Worksheet.Descendants<S.Cell>().Single(cell => cell.CellReference == "A5").InnerText);
        using var printerInput = worksheet.SpreadsheetPrinterSettingsParts.Single().GetStream();
        using var printerCopy = new MemoryStream();
        printerInput.CopyTo(printerCopy);
        Assert.Equal(printerBytes, printerCopy.ToArray());
        Assert.Equal(75u, worksheet.Worksheet.GetFirstChild<S.PageSetup>()!.Scale!.Value);
        var cache = actual.WorkbookPart.PivotTableCacheDefinitionParts.Single();
        File.WriteAllBytes(Path.Combine(evidenceDirectory, recalculate ? "mapped-recalculate.xlsx" : "mapped-cached.xlsx"), output.ToArray());
        Assert.True(cache.PivotCacheDefinition.RefreshOnLoad?.Value);
        Assert.True(cache.PivotCacheDefinition.Invalid?.Value);
        var shared = cache.PivotCacheDefinition.CacheFields!.First().GetFirstChild<S.SharedItems>()!;
        Assert.Equal(new[] { "1", "Two", "New" }, shared.Elements<S.StringItem>().Select(value => value.Val!.Value));
        using var recordsInput = cache.PivotTableCacheRecordsPart!.GetStream();
        var records = XDocument.Load(recordsInput).Root!.Elements().ToArray();
        Assert.Equal("s:New", ResolveRecord(records[0], XElement.Parse(shared.OuterXml).Elements().ToArray()));
        Assert.Equal("s:Two", ResolveRecord(records[1], XElement.Parse(shared.OuterXml).Elements().ToArray()));
        foreach (var pivot in worksheet.PivotTableParts)
        {
            AssertSettings(pivot, cache);
            Assert.Equal(new uint?[] { 1, 0, null, 2 }, pivot.PivotTableDefinition.PivotFields!.First().GetFirstChild<S.Items>()!.Elements<S.Item>().Select(item => item.Index?.Value));
        }
    }

    [Fact]
    public void Opaque_pivot_cache_does_not_prevent_an_independent_supported_cache_update()
    {
        using var input = Fixture();
        string opaqueCachePath;
        string opaqueRecordsPath;
        string opaquePivotPath;
        using (var package = SpreadsheetDocument.Open(input, true))
        {
            var workbook = package.WorkbookPart!;
            var original = workbook.PivotTableCacheDefinitionParts.Single();
            var opaque = workbook.AddNewPart<PivotTableCacheDefinitionPart>();
            var records = opaque.AddNewPart<PivotTableCacheRecordsPart>();
            records.PivotCacheRecords = (S.PivotCacheRecords)original.PivotTableCacheRecordsPart!.PivotCacheRecords.CloneNode(true);
            opaque.PivotCacheDefinition = (S.PivotCacheDefinition)original.PivotCacheDefinition.CloneNode(true);
            opaque.PivotCacheDefinition.Id = opaque.GetIdOfPart(records);
            opaque.PivotCacheDefinition.Append(new S.PivotCacheDefinitionExtensionList(new S.PivotCacheDefinitionExtension
            {
                Uri = "opaque-cache", InnerXml = "<payload xmlns='urn:test'>keep-cache</payload>",
            }));
            workbook.Workbook.GetFirstChild<S.PivotCaches>()!.Append(new S.PivotCache { CacheId = 1, Id = workbook.GetIdOfPart(opaque) });
            var pivot = workbook.WorksheetParts.Single().PivotTableParts.Last();
            pivot.DeletePart(original);
            pivot.AddPart(opaque);
            pivot.PivotTableDefinition.CacheId = 1;
            opaqueCachePath = opaque.Uri.ToString().TrimStart('/');
            opaqueRecordsPath = records.Uri.ToString().TrimStart('/');
            opaquePivotPath = pivot.Uri.ToString().TrimStart('/');
        }

        var before = PivotParts(input);
        var rows = Enumerable.Range(1, 6).Select(row => new OutputRowOccurrence(row, row, 0, 0,
            row == 5 ? new[] { new CellValueEdit(1, "New") } : Array.Empty<CellValueEdit>())).ToArray();
        using var output = new MemoryStream();
        OpenXmlWorkbookWriter.Write(input, output, new WorkbookEditPlan([new SheetEditPlan(1, 6, rows, [], [], false)]));
        var after = PivotParts(output);
        foreach (var path in new[] { opaqueCachePath, opaqueRecordsPath, opaquePivotPath }) Assert.Equal(before[path], after[path]);
        output.Position = 0;
        using var actual = SpreadsheetDocument.Open(output, false);
        var supported = actual.WorkbookPart!.PivotTableCacheDefinitionParts.Single(cache => cache.Uri.ToString().TrimStart('/') != opaqueCachePath);
        Assert.True(supported.PivotCacheDefinition.RefreshOnLoad?.Value);
        Assert.Contains(supported.PivotCacheDefinition.CacheFields!.First().GetFirstChild<S.SharedItems>()!.Elements<S.StringItem>(), item => item.Val == "New");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Pivot_with_printer_settings_still_rejects_row_deletion_or_expansion(int count)
    {
        using var input = Fixture();
        using (var package = SpreadsheetDocument.Open(input, true))
        {
            var sheet = package.WorkbookPart!.WorksheetParts.Single();
            var printer = sheet.AddNewPart<SpreadsheetPrinterSettingsPart>();
            using var bytes = new MemoryStream(new byte[] { 1, 2, 3, 4 });
            printer.FeedData(bytes);
            sheet.Worksheet.Append(new S.PageSetup { Id = sheet.GetIdOfPart(printer) });
        }

        input.Position = 0;
        using var output = new MemoryStream();
        output.Write([7, 8]);
        Assert.Throws<NotSupportedException>(() => ExcelTemplateMapper.Map(input, output, new { items = Enumerable.Range(1, count).ToArray() }));
        Assert.Equal(new byte[] { 7, 8 }, output.ToArray());
    }

    private static string ResolveRecord(XElement record, XElement[] shared)
    {
        var index = (int)record.Elements().First().Attribute("v")!;
        return shared[index].Name.LocalName + ":" + (string?)shared[index].Attribute("v");
    }

    private static void AssertSettings(PivotTablePart pivot, PivotTableCacheDefinitionPart cache)
    {
        var xml = XElement.Parse(pivot.PivotTableDefinition.OuterXml);
        var fields = xml.Element(Ns + "pivotFields")!.Elements().ToArray();
        using var source = cache.GetStream();
        var sharedFields = XDocument.Load(source).Root!.Element(Ns + "cacheFields")!.Elements().ToArray();
        string Resolve(int field, int position)
        {
            var id = (int)fields[field].Element(Ns + "items")!.Elements().ElementAt(position).Attribute("x")!;
            return (string)sharedFields[field].Element(Ns + "sharedItems")!.Elements().ElementAt(id).Attribute("v")!;
        }

        Assert.Equal("Two", Resolve(0, 0));
        Assert.Equal("1", Resolve(0, 1));
        Assert.Equal("1", (string?)fields[0].Element(Ns + "items")!.Elements().First().Attribute("h"));
        Assert.Equal("Saved name", (string?)fields[0].Element(Ns + "items")!.Elements().First().Attribute("n"));
        Assert.Equal("0", (string?)fields[0].Element(Ns + "items")!.Elements().First().Attribute("sd"));
        Assert.Equal("default", (string?)fields[0].Element(Ns + "items")!.Elements().ElementAt(2).Attribute("t"));
        Assert.Equal(new[] { "Two", "1" }, xml.Element(Ns + "rowItems")!.Elements().Select(item => Resolve(0, (int)item.Element(Ns + "x")!.Attribute("v")!)));
        Assert.Equal(new[] { "West", "East" }, xml.Element(Ns + "colItems")!.Elements().Select(item => Resolve(1, (int)item.Element(Ns + "x")!.Attribute("v")!)));
        var page = xml.Element(Ns + "pageFields")!.Elements().Single();
        Assert.Equal("Red", Resolve((int)page.Attribute("fld")!, (int)page.Attribute("item")!));
        Assert.Equal("1", (string?)xml.Element(Ns + "dataFields")!.Elements().Single().Attribute("baseItem"));
        Assert.Equal("A4", (string?)xml.Descendants(Ns + "autoFilter").Single().Attribute("ref"));
        Assert.Equal("Two", (string?)xml.Descendants(Ns + "filter").Single(element => element.Attribute("val") != null).Attribute("val"));
    }

    private static Dictionary<string, byte[]> PivotParts(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, true);
        var result = archive.Entries.Where(entry => entry.FullName.Contains("pivot", StringComparison.OrdinalIgnoreCase)).ToDictionary(entry => entry.FullName, entry =>
        {
            using var source = entry.Open();
            using var bytes = new MemoryStream();
            source.CopyTo(bytes);
            return bytes.ToArray();
        });
        stream.Position = 0;
        return result;
    }

    private static MemoryStream Fixture()
    {
        var input = SdkWorkbook.Create(new S.Worksheet(new S.SheetData(
            new S.Row(SdkWorkbook.Text("A1", "Header")) { RowIndex = 1 },
            new S.Row(SdkWorkbook.Text("A2", "**items[*]")) { RowIndex = 2 },
            new S.Row(SdkWorkbook.Text("A4", "Category"), SdkWorkbook.Text("B4", "Region"), SdkWorkbook.Text("C4", "Color"), SdkWorkbook.Text("D4", "Amount")) { RowIndex = 4 },
            new S.Row(SdkWorkbook.Text("A5", "1"), SdkWorkbook.Text("B5", "East"), SdkWorkbook.Text("C5", "Red"), SdkWorkbook.Number("D5", "10")) { RowIndex = 5 },
            new S.Row(SdkWorkbook.Text("A6", "Two"), SdkWorkbook.Text("B6", "West"), SdkWorkbook.Text("C6", "Blue"), SdkWorkbook.Number("D6", "20")) { RowIndex = 6 })), SdkWorkbook.Styles(), name: "Data");
        using (var document = SpreadsheetDocument.Open(input, true))
        {
            var workbook = document.WorkbookPart!;
            var worksheet = workbook.WorksheetParts.Single();
            var cache = workbook.AddNewPart<PivotTableCacheDefinitionPart>();
            var records = cache.AddNewPart<PivotTableCacheRecordsPart>();
            records.PivotCacheRecords = new S.PivotCacheRecords("""
                <pivotCacheRecords xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="2">
                  <r><x v="0"/><x v="0"/><x v="0"/><x v="0"/></r>
                  <r><x v="1"/><x v="1"/><x v="1"/><x v="1"/></r>
                </pivotCacheRecords>
                """);
            cache.PivotCacheDefinition = new S.PivotCacheDefinition($$"""
                <pivotCacheDefinition xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" r:id="{{cache.GetIdOfPart(records)}}" recordCount="2">
                  <cacheSource type="worksheet"><worksheetSource ref="A4:D6" sheet="Data"/></cacheSource>
                  <cacheFields count="4">
                    <cacheField name="Category"><sharedItems count="2"><s v="1"/><s v="Two"/></sharedItems></cacheField>
                    <cacheField name="Region"><sharedItems count="2"><s v="East"/><s v="West"/></sharedItems></cacheField>
                    <cacheField name="Color"><sharedItems count="2"><s v="Red"/><s v="Blue"/></sharedItems></cacheField>
                    <cacheField name="Amount"><sharedItems count="2" containsString="0" containsNumber="1" containsSemiMixedTypes="0"><n v="10"/><n v="20"/></sharedItems></cacheField>
                  </cacheFields>
                </pivotCacheDefinition>
                """);
            workbook.Workbook.Append(new S.PivotCaches(new S.PivotCache { CacheId = 0, Id = workbook.GetIdOfPart(cache) }));
            for (var index = 0; index < 2; index++)
            {
                var pivot = worksheet.AddNewPart<PivotTablePart>();
                pivot.AddPart(cache);
                pivot.PivotTableDefinition = new S.PivotTableDefinition($$"""
                    <pivotTableDefinition xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" name="Summary{{index}}" cacheId="0" dataCaption="Values">
                      <location ref="{{(index == 0 ? "F4" : "K4")}}" firstHeaderRow="0" firstDataRow="0" firstDataCol="0"/>
                      <pivotFields count="4">
                        <pivotField axis="axisRow" defaultSubtotal="0"><items count="3"><item x="1" h="1" n="Saved name" sd="0"/><item x="0"/><item t="default"/></items></pivotField>
                        <pivotField axis="axisCol" defaultSubtotal="0"><items count="2"><item x="1"/><item x="0"/></items></pivotField>
                        <pivotField axis="axisPage" defaultSubtotal="0"><items count="2"><item x="1"/><item x="0"/></items></pivotField>
                        <pivotField dataField="1"/>
                      </pivotFields>
                      <rowFields count="1"><field x="0"/></rowFields>
                      <rowItems count="2"><i><x v="0"/></i><i><x v="1"/></i></rowItems>
                      <colFields count="1"><field x="1"/></colFields>
                      <colItems count="2"><i><x v="0"/></i><i><x v="1"/></i></colItems>
                      <pageFields count="1"><pageField fld="2" item="1" hier="-1"/></pageFields>
                      <dataFields count="1"><dataField name="Amount" fld="3" baseField="0" baseItem="1"/></dataFields>
                      <filters count="1"><filter fld="0" type="captionEqual" id="0" stringValue1="Two"><autoFilter ref="A4"><filterColumn colId="0"><filters><filter val="Two"/></filters></filterColumn></autoFilter></filter></filters>
                    </pivotTableDefinition>
                    """);
            }
        }

        input.Position = 0;
        using (var document = SpreadsheetDocument.Open(input, false)) Assert.Empty(new OpenXmlValidator().Validate(document).Select(error => error.Description));
        input.Position = 0;
        return input;
    }
}
