using System.Text.Json;
using ExcelRenderer.Formulas.Oracle;
using ExcelRenderer.OpenXml.Calculation;
using ExcelRenderer.OpenXml.Reading;
using Xunit;

namespace ExcelRenderer.OpenXml.Tests;

public sealed class OracleRunnerTests
{
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    public void StagesExecuteIndependently(bool openxmlFails, bool roundtripFails, bool requested)
    {
        var calls = new List<string>();
        object Execute(string stage, bool fails)
        {
            calls.Add(stage);
            return fails ? throw new FormulaCalculationException(stage + " failed") : new { kind = "Number", value = 5 };
        }

        var stages = new List<(string, Func<object>)> { ("openxml", () => Execute("openxml", openxmlFails)) };
        if (requested)
        {
            stages.Add(("roundtrip", () => Execute("roundtrip", roundtripFails)));
        }

        var results = JsonSerializer.SerializeToElement(OracleRunner.ExecuteStages("independent", stages));
        Assert.Equal(requested ? new[] { "openxml", "roundtrip" } : new[] { "openxml" }, calls);
        Assert.Equal(calls.Count, results.GetArrayLength());
        Assert.Equal(openxmlFails ? "Diagnostic" : "Number", results[0].GetProperty("actual").GetProperty("kind").GetString());
        if (requested)
        {
            Assert.Equal(roundtripFails ? "Diagnostic" : "Number", results[1].GetProperty("actual").GetProperty("kind").GetString());
            if (roundtripFails)
            {
                Assert.Equal("roundtrip failed", results[1].GetProperty("actual").GetProperty("value").GetString());
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RealRoundtripFailurePreservesSuccessfulTargetEvaluation(bool mapping)
    {
        using var temp = new TempDirectory();
        WriteCase(temp.Path, "C5", "Z1");
        using var workbook = Fixture.Create("<sheetData><row r=\"1\"><c r=\"A1\"><v>2</v></c><c r=\"B1\"><v>3</v></c><c r=\"Z1\"><f>UNKNOWN()</f></c></row><row r=\"5\"><c r=\"C5\"><f>A1+B1</f></c></row></sheetData>", name: mapping ? "Invoice" : "Case");
        File.WriteAllBytes(Path.Combine(temp.Path, "target-case.xlsx"), workbook.ToArray());
        var args = new[] { Path.Combine(temp.Path, "cases.json"), Path.Combine(temp.Path, "actual.json"), temp.Path, Path.Combine(temp.Path, "saved") };
        Assert.Equal(0, OracleRunner.Run(mapping ? new[] { "--mapping" }.Concat(args).ToArray() : args));
        using var actual = JsonDocument.Parse(File.ReadAllText(args[1]));
        var rows = actual.RootElement.EnumerateArray().ToArray();
        Assert.Equal(mapping ? 2 : 3, rows.Length);
        Assert.Equal(mapping ? "mapping" : "openxml", rows[^2].GetProperty("pipeline").GetString());
        Assert.Equal("Number", rows[^2].GetProperty("actual").GetProperty("kind").GetString());
        Assert.Equal(5, rows[^2].GetProperty("actual").GetProperty("value").GetDouble());
        Assert.Equal("roundtrip", rows[^1].GetProperty("pipeline").GetString());
        Assert.Equal("Diagnostic", rows[^1].GetProperty("actual").GetProperty("kind").GetString());
        Assert.Contains("UnsupportedFunction", rows[^1].GetProperty("actual").GetProperty("value").GetString());
    }

    [Fact]
    public void UnexpectedFailureRetainsCaseStageAndOriginalException()
    {
        var io = new IOException("disk failure");
        var error = Assert.Throws<InvalidOperationException>(() => OracleRunner.ExecuteStages("io-case", new[] { ("roundtrip", (Func<object>)(() => throw io)) }));
        Assert.Contains("io-case", error.Message);
        Assert.Contains("roundtrip", error.Message);
        Assert.Same(io, error.InnerException);
    }

    [Theory]
    [InlineData("C5", false)]
    [InlineData("AA10", false)]
    [InlineData("Z1", false)]
    [InlineData("C5", true)]
    public void TargetIsUsedByEveryRequestedPipeline(string target, bool mapping)
    {
        using var temp = new TempDirectory();
        var other = target == "Z1" ? "Y1" : "Z1";
        var row = target == "C5" ? 5 : target == "AA10" ? 10 : 1;
        var sheet = $"<sheetData><row r=\"1\"><c r=\"A1\"><v>2</v></c><c r=\"B1\"><v>3</v></c><c r=\"{other}\"><v>999</v></c>" +
            (row == 1 ? $"<c r=\"{target}\"><f>A1+B1</f></c></row>" : $"</row><row r=\"{row}\"><c r=\"{target}\"><f>A1+B1</f></c></row>") + "</sheetData>";
        using var workbook = Fixture.Create(sheet, name: mapping ? "Invoice" : "Case");
        File.WriteAllBytes(Path.Combine(temp.Path, "target-case.xlsx"), workbook.ToArray());
        WriteCase(temp.Path, target, other);
        var args = new[] { Path.Combine(temp.Path, "cases.json"), Path.Combine(temp.Path, "actual.json"), temp.Path, Path.Combine(temp.Path, "saved") };
        Assert.Equal(0, OracleRunner.Run(mapping ? new[] { "--mapping" }.Concat(args).ToArray() : args));
        using var actual = JsonDocument.Parse(File.ReadAllText(args[1]));
        Assert.Equal(mapping ? 2 : 3, actual.RootElement.GetArrayLength());
        foreach (var result in actual.RootElement.EnumerateArray())
        {
            Assert.Equal("Number", result.GetProperty("actual").GetProperty("kind").GetString());
            Assert.Equal(5, result.GetProperty("actual").GetProperty("value").GetDouble());
        }

        using var saved = File.OpenRead(Path.Combine(args[3], "target-case.xlsx"));
        using var reader = OpenXmlWorkbookReader.Open(saved);
        Assert.Equal(999, reader.EnumerateCells(1).Single(c => c.Address.Column == (other == "Z1" ? 26 : 25)).Value.Number);
    }

    [Theory]
    [InlineData("A0", "Z1")]
    [InlineData("XFE1", "Z1")]
    [InlineData("A1048577", "Z1")]
    [InlineData("Case!C5", "Z1")]
    [InlineData("C5", "C5")]
    public void InvalidOrOverlappingTargetIsRejected(string target, string input)
    {
        using var temp = new TempDirectory();
        WriteCase(temp.Path, target, input);
        var error = Assert.Throws<InvalidDataException>(() => OracleRunner.Run(new[] { Path.Combine(temp.Path, "cases.json"), Path.Combine(temp.Path, "actual.json") }));
        Assert.Contains("target-case", error.Message);
        Assert.Contains(target, error.Message);
    }

    [Fact]
    public void MissingTargetIdentifiesCaseTargetAndPipeline()
    {
        using var temp = new TempDirectory();
        WriteCase(temp.Path, "C5", "Z1");
        using var workbook = Fixture.Create(Fixture.Cells("<c r=\"Z1\"><v>999</v></c>"));
        File.WriteAllBytes(Path.Combine(temp.Path, "target-case.xlsx"), workbook.ToArray());
        var error = Assert.Throws<InvalidOperationException>(() => OracleRunner.Run(new[] { Path.Combine(temp.Path, "cases.json"), Path.Combine(temp.Path, "actual.json"), temp.Path }));
        Assert.Contains("target-case", error.Message);
        Assert.Contains("C5", error.Message);
        Assert.Contains("openxml", error.Message);
    }

    private static void WriteCase(string directory, string target, string other)
    {
        File.WriteAllText(Path.Combine(directory, "cases.json"), JsonSerializer.Serialize(new[]
        {
            new { id = "target-case", target, formula = "A1+B1", dateSystem = "Excel1900", inputs = new[]
            {
                new { address = "A1", kind = "Number", value = 2 },
                new { address = "B1", kind = "Number", value = 3 },
                new { address = other, kind = "Number", value = 999 },
            } },
        }));
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory() => Directory.CreateDirectory(Path);
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        public void Dispose() => Directory.Delete(Path, true);
    }
}
