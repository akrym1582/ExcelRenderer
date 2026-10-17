using System.Globalization;
using System.Security.Cryptography;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using ExcelRenderer.OpenXml.Model.Cells;
using ExcelRenderer.OpenXml.Writing;
using ExcelRenderer.OpenXml.Writing.Plans;
using ExcelRenderer.TestFixtures;
using Xunit;
using S = DocumentFormat.OpenXml.Spreadsheet;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;

namespace ExcelRenderer.Mapping.Tests;

public sealed class StructuralMappingTests
{
    private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static string FixturePath(string feature) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "ClosedXmlBaseline", feature + ".xlsx");

    public static IEnumerable<object[]> Features()
    {
        foreach (var feature in new[] { "print-area", "static-name", "external-link", "internal-link", "picture", "comment", "table", "filter", "conditional", "validation" })
        foreach (var count in new[] { 0, 3 })
        foreach (var backend in new[] { MappingBackend.OpenXml, MappingBackend.Auto })
            yield return [feature, count, backend];
    }

    [Theory]
    [MemberData(nameof(Features))]
    public void ParentFixturesRetainValuesReferencesAndRelatedParts(string feature, int count, MappingBackend backend)
    {
        using var input = File.OpenRead(FixturePath(feature));
        using var output = new MemoryStream();
        ExcelTemplateMapper.Map(input, output, new { items = Enumerable.Range(1, count).Select(index => new { name = "Item " + index, amount = index * 10 }).ToArray() }, new() { Backend = backend });
        using var actual = WorkbookProbe.Open(output);
        using var expected = WorkbookProbe.Open(FixturePath(feature + "-expected-" + count));
        Assert.Equal(Values(expected), Values(actual));
        Assert.Equal(Names(expected), Names(actual));
        var sheet = actual.Document.WorkbookPart!.WorksheetParts.Single();
        var oldSheet = expected.Document.WorkbookPart!.WorksheetParts.Single();
        Assert.Equal(oldSheet.TableDefinitionParts.Select(part => part.Table.Reference!.Value), sheet.TableDefinitionParts.Select(part => part.Table.Reference!.Value));
        Assert.Equal(oldSheet.Worksheet.GetFirstChild<S.AutoFilter>()?.Reference?.Value, sheet.Worksheet.GetFirstChild<S.AutoFilter>()?.Reference?.Value);
        Assert.Equal(References(oldSheet, "conditionalFormatting"), References(sheet, "conditionalFormatting"));
        Assert.Equal(References(oldSheet, "dataValidation"), References(sheet, "dataValidation"));
        Assert.Equal(oldSheet.WorksheetCommentsPart?.Comments.Descendants<S.Comment>().Select(comment => (comment.Reference!.Value, comment.InnerText)).ToArray() ?? [], sheet.WorksheetCommentsPart?.Comments.Descendants<S.Comment>().Select(comment => (comment.Reference!.Value, comment.InnerText)).ToArray() ?? []);
        Assert.Equal(oldSheet.DrawingsPart?.WorksheetDrawing.Descendants<Xdr.RowId>().Select(row => row.Text), sheet.DrawingsPart?.WorksheetDrawing.Descendants<Xdr.RowId>().Select(row => row.Text));
        Assert.Equal(ImageHashes(oldSheet), ImageHashes(sheet));
        var links = sheet.Worksheet.Descendants<S.Hyperlink>().ToArray();
        Assert.Equal(oldSheet.Worksheet.Descendants<S.Hyperlink>().Select(link => link.Reference!.Value), links.Select(link => link.Reference!.Value));
        foreach (var link in links)
        {
            if (feature == "internal-link") Assert.Equal("Template!A" + (count + 2), link.Location!.Value);
            if (feature == "external-link") Assert.Equal("https://example.invalid/items", sheet.HyperlinkRelationships.Single(item => item.Id == link.Id!.Value).Uri.OriginalString);
        }
        foreach (var vml in sheet.VmlDrawingParts)
        {
            using var source = vml.GetStream();var xml = XDocument.Load(source);
            var shapes = xml.Descendants().Where(element => element.Name.LocalName == "shape").ToArray();
            Assert.Equal(shapes.Length, shapes.Select(shape => (string?)shape.Attribute("id")).Distinct().Count());
            Assert.Equal(Enumerable.Range(1, count).Select(row => row.ToString(CultureInfo.InvariantCulture)), shapes.SelectMany(shape => shape.Descendants().Where(element => element.Name.LocalName == "Row")).Select(element => element.Value));
        }
        AssertNoNewSchemaErrors(expected.Document, actual.Document);
    }

    [Theory]
    [InlineData("pivot", 0)]
    [InlineData("pivot", 3)]
    [InlineData("pivot-values", 0)]
    [InlineData("pivot-values", 3)]
    public void PivotSourceLocationAndValueCachesFollowTheFinalWorksheet(string feature, int count)
    {
        using var input = File.OpenRead(FixturePath(feature));using var output = new MemoryStream();
        ExcelTemplateMapper.Map(input, output, new { items = Enumerable.Range(1, count).ToArray(), amount = 99 });
        using var actual = WorkbookProbe.Open(output);
        var workbook = actual.Document.WorkbookPart!;
        var cache = workbook.Parts.Select(pair => pair.OpenXmlPart).OfType<PivotTableCacheDefinitionPart>().Single();
        using var source = cache.GetStream();var definition = XDocument.Load(source);
        var start = count + 5;
        Assert.Equal($"A{start}:B{start + 2}", (string?)definition.Descendants(Ns + "worksheetSource").Single().Attribute("ref"));
        var pivot = workbook.WorksheetParts.Single().PivotTableParts.Single();
        Assert.Equal($"E{start}", pivot.PivotTableDefinition.Location!.Reference!.Value);
        using var recordsSource = cache.PivotTableCacheRecordsPart!.GetStream();var records = XDocument.Load(recordsSource);
        Assert.DoesNotContain("**amount", records.ToString());
        Assert.DoesNotContain("**amount", definition.ToString());
        var values = definition.Descendants(Ns + "cacheField").Last().Descendants(Ns + "n").Select(value => (string?)value.Attribute("v")).ToArray();
        Assert.Contains(feature == "pivot-values" ? "99" : "10", values);
        using var expected = WorkbookProbe.Open(FixturePath(feature + "-expected-" + count));
        AssertNoNewSchemaErrors(expected.Document, actual.Document);
    }

    [Fact]
    public void SparklineExtensionRetainsTheParentFormulaPreflightRejection()
    {
        using var input = File.OpenRead(FixturePath("sparkline"));using var output = new MemoryStream();output.Write([1,2,3]);
        var error = Assert.Throws<MappingException>(() => ExcelTemplateMapper.Map(input, output, new ThrowingData()));
        Assert.Equal("MappingFormulaNotSupported", error.ReasonCode);Assert.Equal(new byte[] {1,2,3}, output.ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void QuotedCrossSheetNamesLinksAndMixedRuleReferencesMoveWithoutChangingStrings(int count)
    {
        const string target = "O'Brien !";
        using var input = SdkWorkbook.Create(new S.Worksheet(new S.SheetData(new S.Row(SdkWorkbook.Text("A1", "Header")) { RowIndex = 1 }, new S.Row(SdkWorkbook.Text("A2", "**items[*]")) { RowIndex = 2 }, new S.Row(SdkWorkbook.Text("A3", "Footer")) { RowIndex = 3 })), name: target);
        using (var package = SpreadsheetDocument.Open(input, true))
        {
            var workbook = package.WorkbookPart!;
            workbook.Workbook.DefinedNames = new S.DefinedNames(new S.DefinedName("'O''Brien !'!$A$3") { Name = "Footer" },new S.DefinedName("'O''Brien !'!$A$3,'O''Brien !'!$B$3") {Name="Union"});
            var other = SdkWorkbook.AddSheet(workbook,new S.Worksheet(new S.SheetData(new S.Row(SdkWorkbook.Text("A1","link")) { RowIndex=1 })),"Other");
            other.Worksheet.Append(new S.ConditionalFormatting(new S.ConditionalFormattingRule(new S.Formula("AND('O''Brien !'!$A3=\"A3\",LOG10 (100)=2)")) {Type=S.ConditionalFormatValues.Expression,Priority=1}) {SequenceOfReferences=new ListValue<StringValue> {InnerText="A1"}},new S.Hyperlinks(new S.Hyperlink {Reference="A1",Location="'O''Brien !'!A3"}));
            var sheet = workbook.WorksheetParts.First().Worksheet;
            sheet.Append(new S.ConditionalFormatting(new S.ConditionalFormattingRule(new S.Formula("AND(A2=$A$3,$B2=\"A2\",LOG10 (100)=2)")) { Type=S.ConditionalFormatValues.Expression,Priority=1 }) {SequenceOfReferences=new ListValue<StringValue> {InnerText="A2"}});
        }
        input.Position=0;using var output=new MemoryStream();ExcelTemplateMapper.Map(input,output,new {items=Enumerable.Range(1,count).ToArray()});
        using var actual=WorkbookProbe.Open(output);var workbookPart=actual.Document.WorkbookPart!;
        Assert.Contains("$A$"+(count+2),workbookPart.Workbook.DefinedNames!.First().InnerText);
        var otherPart=(WorksheetPart)workbookPart.GetPartById(workbookPart.Workbook.Sheets!.Elements<S.Sheet>().Last().Id!);
        Assert.Equal("'O''Brien !'!A"+(count+2),otherPart.Worksheet.Descendants<S.Hyperlink>().Single().Location!.Value);
        Assert.Equal($"AND('O''Brien !'!$A{count+2}=\"A3\",LOG10 (100)=2)",otherPart.Worksheet.Descendants<S.Formula>().Single().Text);
        var rules=workbookPart.WorksheetParts.First().Worksheet.Elements<S.ConditionalFormatting>().ToArray();Assert.Equal(count,rules.Length);
        for(var index=0;index<count;index++)Assert.Equal($"AND(A{index+2}=$A${count+2},$B{index+2}=\"A2\",LOG10 (100)=2)",rules[index].Descendants<S.Formula>().Single().Text);
    }

    [Theory]
    [InlineData("table")]
    [InlineData("picture")]
    [InlineData("static-name")]
    public void RepeatedStructuresFailBeforeGetterSelectionAndOutput(string feature)
    {
        using var input = new MemoryStream();using(var file=File.OpenRead(FixturePath(feature)))file.CopyTo(input);
        input.Position=0;
        using(var package=SpreadsheetDocument.Open(input,true))
        {
            var workbook=package.WorkbookPart!;var sheet=workbook.WorksheetParts.Single();
            if(feature=="table")sheet.TableDefinitionParts.Single().Table.Reference="A2:B3";
            if(feature=="picture")sheet.DrawingsPart!.WorksheetDrawing.Descendants<Xdr.RowId>().First().Text="1";
            if(feature=="static-name")workbook.Workbook.DefinedNames!.Elements<S.DefinedName>().Single(name=>name.Name=="FooterValue").Text="'Template'!$A$2";
        }
        input.Position=0;using var output=new MemoryStream();output.Write([4,5]);var selected=false;
        Assert.Throws<MappingException>(()=>ExcelTemplateMapper.Map(input,output,new ThrowingData(),new(){BackendSelected=_=>selected=true}));
        Assert.False(selected);Assert.Equal(new byte[]{4,5},output.ToArray());
    }

    [Theory]
    [InlineData(0, typeof(NotSupportedException))]
    [InlineData(-1, typeof(ArgumentOutOfRangeException))]
    [InlineData(99, typeof(ArgumentOutOfRangeException))]
    public void RemovedAndUnknownBackendsFailBeforeReadingInput(int backend, Type error)
    {
        using var input=new UnreadableStream();using var output=new MemoryStream();output.Write([9]);
        var actual=Record.Exception(()=>ExcelTemplateMapper.Map(input,output,new ThrowingData(),new(){Backend=(MappingBackend)backend}));
        Assert.IsType(error,actual);Assert.Equal(new byte[]{9},output.ToArray());
        if(backend==0)Assert.Contains("OpenXml or Auto",actual!.Message);
        Assert.Equal(MappingBackend.OpenXml,new MappingOptions().Backend);
    }

    [Fact]
    public void DirectWriterRejectsRepeatedTableAndMissingRelationshipBeforeCopyingOutput()
    {
        using var input=File.OpenRead(FixturePath("table"));using var output=new MemoryStream();output.Write([9]);
        var rows=Enumerable.Range(1,7).Select(row=>new OutputRowOccurrence(row,row,0,0,Array.Empty<CellValueEdit>())).ToList();
        rows.Insert(6,new OutputRowOccurrence(6,7,1,0,Array.Empty<CellValueEdit>()));
        rows[7]=new OutputRowOccurrence(7,8,0,0,Array.Empty<CellValueEdit>());
        Assert.Throws<NotSupportedException>(()=>OpenXmlWorkbookWriter.Write(input,output,new WorkbookEditPlan([new SheetEditPlan(1,7,rows,[],[],false)])));
        Assert.Equal(new byte[]{9},output.ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void SingleCellPrintAreasCoverEveryOccurrenceAndColumnTitlesSurvive(int count)
    {
        using var input = new MemoryStream();
        using(var source=File.OpenRead(FixturePath("print-area")))source.CopyTo(input);input.Position=0;
        using (var package = SpreadsheetDocument.Open(input, true))
        {
            package.WorkbookPart!.Workbook.DefinedNames = new S.DefinedNames(
                new S.DefinedName("'Template'!$A$2") {Name="_xlnm.Print_Area",LocalSheetId=0},
                new S.DefinedName("'Template'!$A:$C") {Name="_xlnm.Print_Titles",LocalSheetId=0});
        }
        input.Position=0;using var output=new MemoryStream();ExcelTemplateMapper.Map(input,output,new {items=Enumerable.Range(1,count).Select(index=>new {name="Item "+index,amount=index}).ToArray()});
        using var actual=WorkbookProbe.Open(output);
        var area=actual.Reader.Workbook.DefinedNames.SingleOrDefault(name=>name.Name=="_xlnm.Print_Area");
        if(count==0)Assert.Null(area);
        else Assert.Equal("'Template'!$A$2:$A$4",area!.RawText);
        Assert.Equal("'Template'!$A:$C",actual.Reader.Workbook.DefinedNames.Single(name=>name.Name=="_xlnm.Print_Titles").RawText);
    }

    [Fact]
    public void FullHeightPrintAreaStaysInsideTheGridWithoutMaterializingBlankRows()
    {
        using var input=new MemoryStream();using(var source=File.OpenRead(FixturePath("print-area")))source.CopyTo(input);input.Position=0;
        using(var package=SpreadsheetDocument.Open(input,true))package.WorkbookPart!.Workbook.DefinedNames!.Elements<S.DefinedName>().First().Text="'Template'!$A$1:$C$1048576";
        input.Position=0;using var output=new MemoryStream();
        ExcelTemplateMapper.Map(input,output,new {items=Enumerable.Range(1,3).Select(index=>new {name="Item "+index,amount=index}).ToArray()});
        using var actual=WorkbookProbe.Open(output);
        Assert.Equal("'Template'!$A$1:$C$1048576",actual.Reader.Workbook.DefinedNames.Single().RawText);
        Assert.True(actual.Reader.ReadWorksheetInfo(1).Rows.Count<20);
    }

    [Fact]
    public void MissingRelationshipsAndUnsupportedValidationReferencesFailBeforeData()
    {
        foreach(var missingRelationship in new[]{false,true})
        {
            using var input = new MemoryStream();using(var source=File.OpenRead(FixturePath("validation")))source.CopyTo(input);input.Position=0;
            using(var package=SpreadsheetDocument.Open(input,true))
            {
                var sheet=package.WorkbookPart!.WorksheetParts.Single().Worksheet;
                if(missingRelationship)sheet.Append(new S.Hyperlinks(new S.Hyperlink {Reference="A1",Id="missing"}));
                else sheet.Descendants<S.Formula2>().Single().Text="'[Book.xlsx]Other'!$A$1";
            }
            input.Position=0;using var output=new MemoryStream();output.Write([6,7]);
            if(missingRelationship)Assert.Throws<InvalidDataException>(()=>ExcelTemplateMapper.Map(input,output,new ThrowingData()));
            else Assert.Throws<NotSupportedException>(()=>ExcelTemplateMapper.Map(input,output,new ThrowingData()));
            Assert.Equal(new byte[]{6,7},output.ToArray());
        }
    }

    [Fact]
    public void UnknownDrawingExtensionsAreRejectedBeforeDataAndOutput()
    {
        using var input=new MemoryStream();using(var source=File.OpenRead(FixturePath("picture")))source.CopyTo(input);input.Position=0;
        using(var package=SpreadsheetDocument.Open(input,true))
        {
            var drawing=package.WorkbookPart!.WorksheetParts.Single().DrawingsPart!.WorksheetDrawing;
            drawing.Descendants<Xdr.Picture>().Single().ShapeProperties!.Append(new DocumentFormat.OpenXml.Drawing.ExtensionList(new DocumentFormat.OpenXml.Drawing.Extension {Uri="unknown"}));
        }
        input.Position=0;using var output=new MemoryStream();output.Write([7]);
        Assert.Throws<NotSupportedException>(()=>ExcelTemplateMapper.Map(input,output,new ThrowingData()));
        Assert.Equal(new byte[]{7},output.ToArray());
    }

    [Fact]
    public void IdentityWriterKeepsEveryPivotPartByteForByte()
    {
        using var input=File.OpenRead(FixturePath("pivot"));using var output=new MemoryStream();
        var rows=Enumerable.Range(1,8).Select(row=>new OutputRowOccurrence(row,row,0,0,Array.Empty<CellValueEdit>())).ToArray();
        OpenXmlWorkbookWriter.Write(input,output,new WorkbookEditPlan([new SheetEditPlan(1,8,rows,[],[],false)]));
        input.Position=0;output.Position=0;
        using var before=new System.IO.Compression.ZipArchive(input,System.IO.Compression.ZipArchiveMode.Read,true);
        using var after=new System.IO.Compression.ZipArchive(output,System.IO.Compression.ZipArchiveMode.Read,true);
        foreach(var entry in before.Entries)
        {
            using var source=entry.Open();using var actual=after.GetEntry(entry.FullName)!.Open();
            Assert.Equal(SHA256.HashData(source),SHA256.HashData(actual));
        }
    }

    [Fact]
    public void MappingRemovedEnumRetainsItsNumberAndObsoleteMetadata()
    {
        Assert.Equal(0,(int)Enum.Parse<MappingBackend>("ClosedXml"));
        Assert.Equal(1,(int)MappingBackend.OpenXml);Assert.Equal(2,(int)MappingBackend.Auto);
        Assert.NotNull(typeof(MappingBackend).GetField("ClosedXml")!.GetCustomAttributes(typeof(ObsoleteAttribute),false).SingleOrDefault());
    }

    private static (string Address,string Kind,string Value)[] Values(WorkbookProbe book)=>book.Reader.EnumerateCells(1).Where(cell=>cell.Value.Type!=CellValueType.Blank).Select(cell=>(cell.Address.ToString(),cell.Value.Kind,cell.Value.Text??cell.Value.Number?.ToString("R",CultureInfo.InvariantCulture)??cell.Value.Boolean?.ToString()??string.Empty)).ToArray();
    private static string[] Names(WorkbookProbe book)=>book.Reader.Workbook.DefinedNames.Select(name=>name.Name+":"+name.RawText.Replace("'Template'!","Template!")+":"+name.LocalSheetId).ToArray();
    private static string[] ImageHashes(WorksheetPart part)=>part.DrawingsPart?.ImageParts.Select(image=>{using var stream=image.GetStream();return Convert.ToHexString(SHA256.HashData(stream));}).ToArray()??[];
    private static string[] References(WorksheetPart part,string element)
    {
        var xml=XElement.Parse(part.Worksheet.OuterXml);var result=new HashSet<string>();
        foreach(var range in xml.Descendants(Ns+element).SelectMany(item=>((string?)item.Attribute("sqref")??string.Empty).Split(' ',StringSplitOptions.RemoveEmptyEntries)).Select(CellRange.Parse))
        for(var row=range.First.Row;row<=range.Last.Row;row++)for(var column=range.First.Column;column<=range.Last.Column;column++)result.Add(new CellAddress(row,column).ToString());
        return result.Order(StringComparer.Ordinal).ToArray();
    }
    private static void AssertNoNewSchemaErrors(SpreadsheetDocument before,SpreadsheetDocument after)
    {
        var validator=new OpenXmlValidator(FileFormatVersions.Office2013);
        var existing=validator.Validate(before).Select(error=>error.Description).ToHashSet();
        Assert.DoesNotContain(validator.Validate(after).Select(error=>error.Description),error=>!existing.Contains(error));
    }
    private sealed class ThrowingData { public object items=>throw new InvalidOperationException("Data must not be evaluated."); }
    private sealed class UnreadableStream:MemoryStream {public override int Read(byte[] buffer,int offset,int count)=>throw new InvalidOperationException("Input must not be read.");}
}
