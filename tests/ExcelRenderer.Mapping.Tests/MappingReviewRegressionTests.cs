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

namespace ExcelRenderer.Mapping.Tests;

public sealed class MappingReviewRegressionTests
{
    private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void MovedPivotKeepsHiddenItemsAndTheirOrder()
    {
        using var input = new MemoryStream();
        using (var file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ClosedXmlBaseline", "pivot.xlsx"))) file.CopyTo(input);
        input.Position = 0;
        using (var document = SpreadsheetDocument.Open(input, true))
        {
            var pivot = document.WorkbookPart!.WorksheetParts.Single().PivotTableParts.Single();
            var items = pivot.PivotTableDefinition.PivotFields!.First().GetFirstChild<S.Items>()!;
            items.RemoveAllChildren();
            items.Append(new S.Item { Index = 1, Hidden = true }, new S.Item { Index = 0, ItemName = "First" });
        }

        input.Position = 0;
        using var output = new MemoryStream();
        ExcelTemplateMapper.Map(input, output, new { items = new[] { 1, 2, 3 } });
        output.Position = 0;
        using var actual = SpreadsheetDocument.Open(output, false);
        var saved = actual.WorkbookPart!.WorksheetParts.Single().PivotTableParts.Single().PivotTableDefinition.PivotFields!.First().GetFirstChild<S.Items>()!.Elements<S.Item>().ToArray();
        Assert.Equal(new uint[] { 1, 0 }, saved.Select(item => item.Index!.Value));
        Assert.True(saved[0].Hidden!.Value);
        Assert.Equal("First", saved[1].ItemName!.Value);
    }

    [Theory]
    [InlineData("B2:B3 D2:D3")]
    [InlineData("B2:B3 B6:B7")]
    public void IdentityRulesKeepTheirSharedBaseAndApplicationSet(string ranges)
    {
        using var input = Rules(ranges, "AND(B2>0,$B2>0,B$2>0,$B$2>0)");
        using var output = new MemoryStream();
        ExcelTemplateMapper.Map(input, output, new { items = new[] { 1 } });
        output.Position = 0;
        using var actual = SpreadsheetDocument.Open(output, false);
        var rules = actual.WorkbookPart!.WorksheetParts.Single().Worksheet.Elements<S.ConditionalFormatting>().ToArray();
        Assert.Single(rules);
        Assert.Equal(ranges, rules[0].SequenceOfReferences!.InnerText);
        Assert.Equal("AND(B2>0,$B2>0,B$2>0,$B$2>0)", rules[0].Descendants<S.Formula>().Single().Text);
    }

    [Fact]
    public void FormulaThresholdMovesWithTheSameReferenceAsFormulaElements()
    {
        using var input = Rules("B3 D3", "$A$3", threshold: true);
        using var output = new MemoryStream();
        ExcelTemplateMapper.Map(input, output, new { items = new[] { 1, 2, 3 } });
        output.Position = 0;
        using var actual = SpreadsheetDocument.Open(output, false);
        var values = actual.WorkbookPart!.WorksheetParts.Single().Worksheet.Descendants<S.ConditionalFormatValueObject>().Where(value => value.Type?.Value == S.ConditionalFormatValueObjectValues.Formula).ToArray();
        Assert.NotEmpty(values);
        Assert.All(values, value => Assert.Equal("$A$5", value.Val!.Value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void MixedRulesAndValidationKeepCellTargetsAfterInsertionOrDeletion(int count)
    {
        const string formula = "AND(B3>0,$B3>0,B$3>0,$B$3>0,\"A2\"=\"A2\",LOG10 (100)=2)";
        using var input = Rules("B3:B4 D3:D4", formula);
        using (var document = SpreadsheetDocument.Open(input, true))
        {
            document.WorkbookPart!.WorksheetParts.Single().Worksheet.Append(new S.DataValidations(
                new S.DataValidation(new S.Formula1(formula))
                {
                    Type = S.DataValidationValues.Custom,
                    SequenceOfReferences = new ListValue<StringValue> { InnerText = "B3:B4 D3:D4" },
                    AllowBlank = true, ShowInputMessage = true, PromptTitle = "Title", Prompt = "Keep me",
                }) { Count = 1 });
        }

        input.Position = 0;
        using var output = new MemoryStream();
        ExcelTemplateMapper.Map(input, output, new { items = Enumerable.Range(1, count).ToArray() });
        output.Position = 0;
        using var actual = SpreadsheetDocument.Open(output, false);
        var sheet = actual.WorkbookPart!.WorksheetParts.Single().Worksheet;
        var first = count + 2;
        var expectedCells = new[] { $"B{first}", $"B{first + 1}", $"D{first}", $"D{first + 1}" }.Order().ToArray();
        Assert.Equal(expectedCells, AppliedFormulas(sheet, "conditionalFormatting", "formula").Keys.Order());
        foreach (var kind in new[] { ("conditionalFormatting", "formula"), ("dataValidation", "formula1") })
        {
            foreach (var cell in AppliedFormulas(sheet, kind.Item1, kind.Item2))
            {
                var address = CellAddress.Parse(cell.Key);
                var column = cell.Key[0];
                Assert.Equal($"AND({cell.Key}>0,$B{address.Row}>0,{column}${first}>0,$B${first}>0,\"A2\"=\"A2\",LOG10 (100)=2)", cell.Value);
            }
        }

        Assert.All(sheet.Descendants<S.DataValidation>(), validation =>
        {
            Assert.True(validation.AllowBlank!.Value);
            Assert.Equal("Keep me", validation.Prompt!.Value);
            Assert.Equal("Title", validation.PromptTitle!.Value);
        });
        Assert.All(sheet.Descendants<S.ConditionalFormattingRule>(), rule => Assert.True(rule.StopIfTrue!.Value));
        Assert.Empty(new OpenXmlValidator().Validate(actual));
    }

    [Fact]
    public void DifferentStructuralOffsetsSplitAndRebaseRulesInBothDirections()
    {
        using var input = Rules("B1 B3 D3", "AND(B3>0,$B3>0,B$3>0,$B$3>0)");
        using var output = new MemoryStream();
        ExcelTemplateMapper.Map(input, output, new { items = new[] { 1, 2, 3 } });
        output.Position = 0;
        using var actual = SpreadsheetDocument.Open(output, false);
        var formulas = AppliedFormulas(actual.WorkbookPart!.WorksheetParts.Single().Worksheet, "conditionalFormatting", "formula");
        Assert.Equal(new[] { "B1", "B5", "D5" }, formulas.Keys.Order());
        Assert.Equal("AND(B5>0,$B5>0,B$5>0,$B$5>0)", formulas["B1"]);
        Assert.Equal("AND(B7>0,$B7>0,B$5>0,$B$5>0)", formulas["B5"]);
        Assert.Equal("AND(D7>0,$B7>0,D$5>0,$B$5>0)", formulas["D5"]);
        Assert.Empty(new OpenXmlValidator().Validate(actual));
    }

    [Theory]
    [InlineData("colorScale")]
    [InlineData("dataBar")]
    [InlineData("iconSet")]
    public void EveryStandardThresholdUsesFormulaStoresAndPreservesNumericThresholds(string kind)
    {
        using var input = Rules("B3 D3", "$A$3", threshold: true);
        using (var document = SpreadsheetDocument.Open(input, true))
        {
            var sheet = document.WorkbookPart!.WorksheetParts.Single().Worksheet;
            var conditional = sheet.Elements<S.ConditionalFormatting>().Single();
            var rule = conditional.Elements<S.ConditionalFormattingRule>().Single();
            var xml = XElement.Parse(rule.OuterXml);
            xml.SetAttributeValue("type", kind);
            var thresholds = new[]
            {
                new XElement(Ns + "cfvo", new XAttribute("type", "formula"), new XAttribute("val", "SUM(A3,$A3,A$3,$A$3,'O''Brien !'!$A$3)")),
                new XElement(Ns + "cfvo", new XAttribute("type", "percent"), new XAttribute("val", 50)),
            };
            var body = new XElement(Ns + kind, thresholds);
            if (kind == "iconSet") body.Add(new XElement(Ns + "cfvo", new XAttribute("type", "max")));
            if (kind is "colorScale" or "dataBar") body.Add(new XElement(Ns + "color", new XAttribute("rgb", "FFFF0000")));
            if (kind == "colorScale") body.Add(new XElement(Ns + "color", new XAttribute("rgb", "FF00FF00")));
            xml.ReplaceNodes(body);
            rule.InnerXml = string.Concat(xml.Elements().Select(element => element.ToString()));
            SdkWorkbook.AddSheet(document.WorkbookPart, new S.Worksheet(new S.SheetData(
                new S.Row(SdkWorkbook.Text("A1", "Header")) { RowIndex = 1 },
                new S.Row(SdkWorkbook.Text("A2", "**items[*]")) { RowIndex = 2 },
                new S.Row(SdkWorkbook.Number("A3", "10")) { RowIndex = 3 })), "O'Brien !");
        }

        input.Position = 0;
        using var output = new MemoryStream();
        ExcelTemplateMapper.Map(input, output, new { items = new[] { 1, 2, 3 } });
        output.Position = 0;
        using var actual = SpreadsheetDocument.Open(output, false);
        var sheetPart = actual.WorkbookPart!.WorksheetParts.Single(part => part.Worksheet.Elements<S.ConditionalFormatting>().Any());
        var savedRule = sheetPart.Worksheet.Elements<S.ConditionalFormatting>().Single();
        Assert.Equal("B5 D5", savedRule.SequenceOfReferences!.InnerText);
        var thresholdsAfter = savedRule.Descendants<S.ConditionalFormatValueObject>().ToArray();
        Assert.Equal("SUM(A5,$A5,A$5,$A$5,'O''Brien !'!$A$5)", thresholdsAfter[0].Val!.Value);
        Assert.Equal("50", thresholdsAfter[1].Val!.Value);
        Assert.Empty(new OpenXmlValidator().Validate(actual));
    }

    [Theory]
    [InlineData("'[Book.xlsx]Other'!$A$1")]
    [InlineData("SUM(Table1[Value])")]
    [InlineData("SUM(Sheet1:Sheet3!A1)")]
    [InlineData("A1#")]
    public void UnsupportedThresholdFailsBeforeDataAndPreservesStreamAndFile(string formula)
    {
        using var input = Rules("B3", formula, threshold: true);
        using var output = new MemoryStream();
        output.Write([4, 5, 6]);
        output.Position = 1;
        Assert.Throws<NotSupportedException>(() => ExcelTemplateMapper.Map(input, output, new ThrowingData()));
        Assert.Equal(new byte[] { 4, 5, 6 }, output.ToArray());
        Assert.Equal(1, output.Position);
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var template = Path.Combine(directory, "template.xlsx");
            var destination = Path.Combine(directory, "output.xlsx");
            File.WriteAllBytes(template, input.ToArray());
            File.WriteAllBytes(destination, [7, 8]);
            Assert.Throws<NotSupportedException>(() => ExcelTemplateMapper.Map(template, destination, new ThrowingData()));
            Assert.Equal(new byte[] { 7, 8 }, File.ReadAllBytes(destination));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void PopulationRulesKeepAllRangesAndRemoveDeletedApplications()
    {
        using var input = Rules("B2 D3", "$A$3", threshold: true);
        using var output = new MemoryStream();
        ExcelTemplateMapper.Map(input, output, new { items = Array.Empty<int>() });
        output.Position = 0;
        using var actual = SpreadsheetDocument.Open(output, false);
        var rule = actual.WorkbookPart!.WorksheetParts.Single().Worksheet.Elements<S.ConditionalFormatting>().Single();
        Assert.Equal("D2", rule.SequenceOfReferences!.InnerText);
        Assert.Equal("$A$2", rule.Descendants<S.ConditionalFormatValueObject>().First().Val!.Value);
        Assert.Empty(new OpenXmlValidator().Validate(actual));
    }

    [Fact]
    public void MinMaxPopulationIsNeverSplitAcrossOccurrences()
    {
        using var input = Rules("B2 D2", "$A$3", threshold: true);
        using (var document = SpreadsheetDocument.Open(input, true))
        {
            var threshold = document.WorkbookPart!.WorksheetParts.Single().Worksheet.Descendants<S.ConditionalFormatValueObject>().First();
            threshold.Type = S.ConditionalFormatValueObjectValues.Min;
            threshold.Val = null;
        }

        input.Position = 0;
        using var output = new MemoryStream();
        ExcelTemplateMapper.Map(input, output, new { items = new[] { 1, 2, 3 } });
        output.Position = 0;
        using var actual = SpreadsheetDocument.Open(output, false);
        var rule = actual.WorkbookPart!.WorksheetParts.Single().Worksheet.Elements<S.ConditionalFormatting>().Single();
        Assert.Equal(new[] { "B2", "B3", "B4", "D2", "D3", "D4" }, CoveredCells(rule.SequenceOfReferences!.InnerText!).Order());
        Assert.Empty(new OpenXmlValidator().Validate(actual));
    }

    [Fact]
    public void NestedOccurrencesResolveRelativeReferencesToTheirOwnParentRows()
    {
        using var input = Rules("B3 D3", "AND(B3>0,$A2>0)");
        var rows = new[]
        {
            new OutputRowOccurrence(1, 1, 0, 0, []),
            new OutputRowOccurrence(2, 2, 1, 0, []),
            new OutputRowOccurrence(3, 3, 2, 1, []),
            new OutputRowOccurrence(3, 4, 3, 1, []),
            new OutputRowOccurrence(4, 5, 1, 0, []),
            new OutputRowOccurrence(2, 6, 4, 0, []),
            new OutputRowOccurrence(3, 7, 5, 4, []),
            new OutputRowOccurrence(4, 8, 4, 0, []),
        };
        using var output = new MemoryStream();
        OpenXmlWorkbookWriter.Write(input, output, new WorkbookEditPlan([new SheetEditPlan(1, 4, rows, [], [], false)]));
        output.Position = 0;
        using var actual = SpreadsheetDocument.Open(output, false);
        var formulas = AppliedFormulas(actual.WorkbookPart!.WorksheetParts.Single().Worksheet, "conditionalFormatting", "formula");
        Assert.Equal(new[] { "B3", "B4", "B7", "D3", "D4", "D7" }, formulas.Keys.Order());
        foreach (var pair in formulas)
        {
            Assert.Equal($"AND({pair.Key}>0,$A{(pair.Key.EndsWith('7') ? 6 : 2)}>0)", pair.Value);
        }
        Assert.Empty(new OpenXmlValidator().Validate(actual));
    }

    [Fact]
    public void ImplicitTrailingRowsAreSplitWhereRelativeTargetsCrossExpandedRows()
    {
        using var input = Rules("B4:B8", "A2>0");
        using var output = new MemoryStream();
        ExcelTemplateMapper.Map(input, output, new { items = new[] { 1, 2, 3 } });
        output.Position = 0;
        using var actual = SpreadsheetDocument.Open(output, false);
        var formulas = AppliedFormulas(actual.WorkbookPart!.WorksheetParts.Single().Worksheet, "conditionalFormatting", "formula");
        Assert.Equal("A2>0", formulas["B6"]);
        Assert.Equal("A5>0", formulas["B7"]);
        Assert.Equal("A6>0", formulas["B8"]);
        Assert.Equal("A7>0", formulas["B9"]);
        Assert.Equal("A8>0", formulas["B10"]);
    }

    [Fact]
    public void PopulationThatCannotShareOneFormulaFailsWithoutPublishingStage()
    {
        using var input = Rules("B1 B3", "B3", threshold: true);
        using var output = new MemoryStream();
        output.Write([9, 8, 7]);
        output.Position = 1;
        var error = Assert.Throws<NotSupportedException>(() => ExcelTemplateMapper.Map(input, output, new { items = new[] { 1, 2, 3 } }));
        Assert.Contains("population-dependent", error.Message);
        Assert.Equal(new byte[] { 9, 8, 7 }, output.ToArray());
        Assert.Equal(1, output.Position);
    }

    [Fact]
    public void DirectWriterInspectsThresholdSyntaxBeforeCopyingStagedOutput()
    {
        using var input = Rules("B3", "Table1[Amount]", threshold: true);
        using var output = new MemoryStream();
        output.Write([9, 8]);
        output.Position = 1;
        var rows = new[]
        {
            new OutputRowOccurrence(1, 1, 0, 0, []),
            new OutputRowOccurrence(2, 2, 1, 0, []),
            new OutputRowOccurrence(2, 3, 2, 0, []),
            new OutputRowOccurrence(3, 4, 0, 0, []),
        };
        Assert.Throws<NotSupportedException>(() => OpenXmlWorkbookWriter.Write(input, output, new WorkbookEditPlan([new SheetEditPlan(1, 3, rows, [], [], false)])));
        Assert.Equal(new byte[] { 9, 8 }, output.ToArray());
        Assert.Equal(1, output.Position);
    }

    [Fact]
    public void ThresholdAndElementFormulasUseIdenticalTransformationAndIdentityPreservesSettings()
    {
        using var input = Rules("B3 D3", "SUM(A3,$A3,A$3,$A$3)", threshold: true);
        using (var document = SpreadsheetDocument.Open(input, true))
        {
            document.WorkbookPart!.WorksheetParts.Single().Worksheet.Append(
                new S.ConditionalFormatting(new S.ConditionalFormattingRule(new S.Formula("SUM(A3,$A3,A$3,$A$3)"))
                { Type = S.ConditionalFormatValues.Expression, Priority = 2 })
                { SequenceOfReferences = new ListValue<StringValue> { InnerText = "B3 D3" } });
        }

        input.Position = 0;
        using var identity = new MemoryStream();
        var rows = Enumerable.Range(1, 3).Select(row => new OutputRowOccurrence(row, row, 0, 0, [])).ToArray();
        OpenXmlWorkbookWriter.Write(input, identity, new WorkbookEditPlan([new SheetEditPlan(1, 3, rows, [], [], false)]));
        identity.Position = 0;
        using (var saved = SpreadsheetDocument.Open(identity, false))
        {
            Assert.Equal("SUM(A3,$A3,A$3,$A$3)", saved.WorkbookPart!.WorksheetParts.Single().Worksheet.Descendants<S.ConditionalFormatValueObject>().First().Val!.Value);
        }
        input.Position = 0;
        using var output = new MemoryStream();
        ExcelTemplateMapper.Map(input, output, new { items = new[] { 1, 2, 3 } });
        output.Position = 0;
        using var actual = SpreadsheetDocument.Open(output, false);
        var sheet = actual.WorkbookPart!.WorksheetParts.Single().Worksheet;
        Assert.Equal(sheet.Descendants<S.Formula>().Single().Text, sheet.Descendants<S.ConditionalFormatValueObject>().First().Val!.Value);
        Assert.Equal("SUM(A5,$A5,A$5,$A$5)", sheet.Descendants<S.Formula>().Single().Text);
        Assert.Empty(new OpenXmlValidator().Validate(actual));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void RelativeListValidationKeepsItsRangeAndMessages(int count)
    {
        using var input = Rules("B3 D3", "B3>0");
        using (var document = SpreadsheetDocument.Open(input, true))
        {
            document.WorkbookPart!.WorksheetParts.Single().Worksheet.Append(new S.DataValidations(
                new S.DataValidation(new S.Formula1("$A3:$A4"))
                { Type = S.DataValidationValues.List, AllowBlank = true, Error = "Select a saved value", ShowErrorMessage = true,
                  SequenceOfReferences = new DocumentFormat.OpenXml.ListValue<DocumentFormat.OpenXml.StringValue> { InnerText = "B3 D3" } }) { Count = 1 });
        }
        input.Position = 0;
        using var output = new MemoryStream();
        ExcelTemplateMapper.Map(input, output, new { items = Enumerable.Range(1, count).ToArray() });
        output.Position = 0;
        using var actual = SpreadsheetDocument.Open(output, false);
        var validation = actual.WorkbookPart!.WorksheetParts.Single().Worksheet.Descendants<S.DataValidation>().Single();
        Assert.Equal($"B{count + 2} D{count + 2}", validation.SequenceOfReferences!.InnerText);
        Assert.Equal($"$A{count + 2}:$A{count + 3}", validation.Formula1!.Text);
        Assert.Equal("Select a saved value", validation.Error!.Value);
        Assert.True(validation.ShowErrorMessage!.Value);
        Assert.Empty(new OpenXmlValidator().Validate(actual));
    }

    [Fact]
    public void CompletelyDeletedApplicationsRemoveTheirRulesAndValidationContainer()
    {
        using var input = Rules("B2 D2", "B2>0");
        using (var document = SpreadsheetDocument.Open(input, true))
        {
            document.WorkbookPart!.WorksheetParts.Single().Worksheet.Append(new S.DataValidations(
                new S.DataValidation(new S.Formula1("B2>0")) { Type = S.DataValidationValues.Custom,
                SequenceOfReferences = new ListValue<StringValue> { InnerText = "B2 D2" } }) { Count = 1 });
        }
        input.Position = 0;
        using var output = new MemoryStream();
        ExcelTemplateMapper.Map(input, output, new { items = Array.Empty<int>() });
        output.Position = 0;
        using var actual = SpreadsheetDocument.Open(output, false);
        var sheet = actual.WorkbookPart!.WorksheetParts.Single().Worksheet;
        Assert.Empty(sheet.Elements<S.ConditionalFormatting>());
        Assert.Null(sheet.GetFirstChild<S.DataValidations>());
        Assert.Empty(new OpenXmlValidator().Validate(actual));
    }

    [Fact]
    public void MixedExpressionAndPopulationRulesCanKeepDifferentRangePartitions()
    {
        using var input = Rules("B1 B3", "B3>0");
        using (var document = SpreadsheetDocument.Open(input, true))
        {
            document.WorkbookPart!.WorksheetParts.Single().Worksheet.Elements<S.ConditionalFormatting>().Single().Append(
                new S.ConditionalFormattingRule(new S.ColorScale(
                    new S.ConditionalFormatValueObject { Type = S.ConditionalFormatValueObjectValues.Min },
                    new S.ConditionalFormatValueObject { Type = S.ConditionalFormatValueObjectValues.Max },
                    new S.Color { Rgb = "FFFF0000" }, new S.Color { Rgb = "FF00FF00" })) { Type = S.ConditionalFormatValues.ColorScale, Priority = 2 });
        }
        input.Position = 0;
        using var output = new MemoryStream();
        ExcelTemplateMapper.Map(input, output, new { items = new[] { 1, 2, 3 } });
        output.Position = 0;
        using var actual = SpreadsheetDocument.Open(output, false);
        var rules = actual.WorkbookPart!.WorksheetParts.Single().Worksheet.Elements<S.ConditionalFormatting>().ToArray();
        var population = Assert.Single(rules, rule => rule.Descendants<S.ColorScale>().Any());
        Assert.Equal("B1 B5", population.SequenceOfReferences!.InnerText);
        Assert.Equal(2, rules.Count(rule => rule.Descendants<S.Formula>().Any()));
        Assert.Empty(new OpenXmlValidator().Validate(actual));
    }

    private static Dictionary<string, string> AppliedFormulas(S.Worksheet sheet, string element, string formula)
    {
        var result = new Dictionary<string, string>();
        foreach (var rule in XElement.Parse(sheet.OuterXml).Descendants(Ns + element))
        {
            var reference = (string)rule.Attribute("sqref")!;
            var anchor = CellRange.Parse(reference.Split(' ')[0]).First;
            var text = rule.Descendants(Ns + formula).Single().Value;
            foreach (var cell in CoveredCells(reference))
            {
                var address = CellAddress.Parse(cell);
                // Test-only A1 copy semantics, independent of the writer's tokenizer and row plan.
                var applied = System.Text.RegularExpressions.Regex.Replace(text,
                    """(?<![A-Za-z0-9_"])(?<col>\$?[A-Z]+)(?<row>\$?[1-9][0-9]*)(?![A-Za-z0-9_"])""", match =>
                    {
                        if (text[(match.Index + match.Length)..].TrimStart().StartsWith('(')) return match.Value;
                        var col = match.Groups["col"].Value;
                        var row = match.Groups["row"].Value;
                        var source = CellAddress.Parse((col + row).Replace("$", ""));
                        var moved = new CellAddress(source.Row + (row[0] == '$' ? 0 : address.Row - anchor.Row), source.Column + (col[0] == '$' ? 0 : address.Column - anchor.Column)).ToString();
                        var letters = new string(moved.TakeWhile(char.IsLetter).ToArray());
                        return (col[0] == '$' ? "$" : "") + letters + (row[0] == '$' ? "$" : "") + new string(moved.SkipWhile(char.IsLetter).ToArray());
                    });
                result.Add(cell, applied);
            }
        }

        return result;
    }

    private static IEnumerable<string> CoveredCells(string reference)
    {
        foreach (var token in reference.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var range = CellRange.Parse(token);
            for (var row = range.First.Row; row <= range.Last.Row; row++)
            for (var column = range.First.Column; column <= range.Last.Column; column++)
                yield return new CellAddress(row, column).ToString();
        }
    }

    private sealed class ThrowingData
    {
        public object items => throw new InvalidOperationException("Data must not be evaluated.");
    }

    private static MemoryStream Rules(string ranges, string formula, bool threshold = false)
    {
        var rule = threshold
            ? new S.ConditionalFormattingRule(new S.ColorScale(
                new S.ConditionalFormatValueObject { Type = S.ConditionalFormatValueObjectValues.Formula, Val = formula },
                new S.ConditionalFormatValueObject { Type = S.ConditionalFormatValueObjectValues.Max },
                new S.Color { Rgb = "FFFF0000" }, new S.Color { Rgb = "FF00FF00" })) { Type = S.ConditionalFormatValues.ColorScale, Priority = 1 }
            : new S.ConditionalFormattingRule(new S.Formula(formula)) { Type = S.ConditionalFormatValues.Expression, Priority = 1, StopIfTrue = true };
        return SdkWorkbook.Create(new S.Worksheet(new S.SheetData(
            new S.Row(SdkWorkbook.Text("A1", "Header")) { RowIndex = 1 },
            new S.Row(SdkWorkbook.Text("A2", "**items[*]")) { RowIndex = 2 },
            new S.Row(SdkWorkbook.Number("A3", "10")) { RowIndex = 3 }),
            new S.ConditionalFormatting(rule) { SequenceOfReferences = new ListValue<StringValue> { InnerText = ranges } }), SdkWorkbook.Styles());
    }
}
