using ExcelRenderer.OpenXml.Calculation;
using ExcelRenderer.OpenXml.Input;
using ExcelRenderer.OpenXml.Reading;
using ExcelRenderer.OpenXml.Writing;
using ExcelRenderer.OpenXml.Writing.Plans;
using Xunit;

namespace ExcelRenderer.OpenXml.Tests;

public sealed class IntegrationBoundaryTests
{
    [Theory]
    [InlineData("SUM(A1,$B2,C$3,$D$4,\"A1\",'A1 sheet'!A1,LOG10(A1),Name_A1)", "SUM(B3,$B4,D$3,$D$4,\"A1\",'A1 sheet'!B3,LOG10(B3),Name_A1)")]
    [InlineData("1E10+2.5E-3+A1", "1E10+2.5E-3+B3")]
    [InlineData("SUM(A:C,$D:$E,1:3,$4:$5)", "SUM(B:D,$D:$E,3:5,$4:$5)")]
    [InlineData("SUM(A1:B2)+Sheet1!A1", "SUM(B3:C4)+Sheet1!B3")]
    [InlineData("IF('O''Brien'!A1=0,\"-\",\"(\"&A1&\")\")", "IF('O''Brien'!B3=0,\"-\",\"(\"&B3&\")\")")]
    public void SharedFormulaReferencesAreTranslatedWithoutChangingLiteralsOrNames(string master, string expected)
    {
        using var input = Fixture.Create($"<sheetData><row r=\"1\"><c r=\"A1\"><f t=\"shared\" si=\"0\" ref=\"A1:B3\">{System.Security.SecurityElement.Escape(master)}</f><v>1</v></c></row><row r=\"3\"><c r=\"B3\"><f t=\"shared\" si=\"0\"/><v>2</v></c></row></sheetData>");
        using var reader = OpenXmlWorkbookReader.Open(input);
        var follower = reader.EnumerateCells(1).Last();
        Assert.True(FormulaTextResolver.TryResolve(follower, out var text));
        Assert.Equal(expected, text);
    }

    [Theory]
    [InlineData("Table1[A1]")]
    [InlineData("A1#")]
    [InlineData("'unterminated")]
    public void UnknownSharedFormulaGrammarDoesNotReturnTheMasterFormula(string master)
    {
        using var input = Fixture.Create($"<sheetData><row><c r=\"A1\"><f t=\"shared\" si=\"0\" ref=\"A1:B1\">{System.Security.SecurityElement.Escape(master)}</f><v>1</v></c><c r=\"B1\"><f t=\"shared\" si=\"0\"/><v>2</v></c></row></sheetData>");
        using var reader = OpenXmlWorkbookReader.Open(input);
        Assert.False(FormulaTextResolver.TryResolve(reader.EnumerateCells(1).Last(), out var text));
        Assert.Null(text);
    }

    [Fact]
    public void WriterCreatesMissingStylePartAndPreservesEscapedLiteralText()
    {
        using var input = Fixture.Create("<sheetData><row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>template</t></is></c></row></sheetData>");
        using var output = new MemoryStream();
        var date = new DateTime(2026, 10, 16);
        var text = "_x0041_\u0001 =SUM(A1)";
        var row = new OutputRowOccurrence(1, 1, 0, 0, new[] { new CellValueEdit(1, date), new CellValueEdit(2, text) });
        OpenXmlWorkbookWriter.Write(input, output, new WorkbookEditPlan(new[] { new SheetEditPlan(1, 1, new[] { row }, Array.Empty<int>(), Array.Empty<int>(), false) }));
        output.Position = 0;
        using (var reader = OpenXmlWorkbookReader.Open(output))
        {
            var cells = reader.EnumerateCells(1).ToArray();
            Assert.Equal(text, cells[1].Value.Text);
            Assert.Equal(14, reader.ResolveStyle(cells[0]).NumberFormatId);
        }

        output.Position = 0;
        using var package = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(output, false);
        Assert.NotNull(package.WorkbookPart!.WorkbookStylesPart);
        Assert.Empty(new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(package));
    }

    [Fact]
    public async Task SharedInputPreparationSpillsAndLeavesIndependentCursorsAndCallerOpen()
    {
        var directory = Directory.CreateTempSubdirectory("openxml-input-");
        try
        {
            using var input = Fixture.Create("<sheetData><row><c t=\"inlineStr\"><is><t>source</t></is></c></row></sheetData>");
            var options = new WorkbookInputOptions { MemoryThresholdBytes = 16, AllowTemporaryFiles = true, TemporaryDirectory = directory.FullName };
            using (var owner = await WorkbookInputPreparer.ReadAsync(input, options, default))
            {
                using var first = owner.OpenRead();
                using var second = owner.OpenRead();
                Assert.Equal(first.ReadByte(), second.ReadByte());
                first.Position = first.Length;
                Assert.Equal(1, second.Position);
                Assert.Single(directory.GetFiles());
            }

            Assert.True(input.CanRead);
            Assert.Empty(directory.GetFiles());
            input.Position = 0;
            await Assert.ThrowsAsync<InvalidDataException>(() => WorkbookInputPreparer.ReadAsync(input, new WorkbookInputOptions { MaxInputBytes = 1 }, default));
            Assert.True(input.CanRead);
        }
        finally
        {
            directory.Delete(true);
        }
    }
}
