using Xunit;

namespace ExcelRenderer.Formulas.Tests;

public sealed class LookupIndexTests
{
    [Fact]
    public void HorizontalIncrementalIndexesPreservePositionsAsTheirSuffixShrinks()
    {
        var builder = new InMemoryFormulaWorkbook.Builder().AddSheet("S");
        for (int i = 1; i <= 1000; i++) builder.SetValue(new("S", 2, i), FormulaValue.FromNumber(i));
        var session = new FormulaEngine().CreateSession(builder.Build());
        for (int i = 1; i <= 1000; i++) Assert.Equal(i, session.Evaluate(FormulaParser.Parse($"MATCH({i},A2:ALL2,0)").Formula!, new("S", 3, 1)).Value.Number);
        Assert.Equal(1000, session.GetStatistics().RangeCandidates);
        Assert.Equal(1000, session.GetStatistics().SourceReads);
    }

    [Fact]
    public void EarlyWildcardMatchesDoNotScanTheWholeCachedTextIndex()
    {
        var builder = new InMemoryFormulaWorkbook.Builder().AddSheet("S");
        for (int i = 1; i <= 10000; i++) builder.SetValue(new("S", i, 1), FormulaValue.FromText($"item{i}"));
        var session = new FormulaEngine().CreateSession(builder.Build());
        Assert.Equal("#N/A", Evaluate(session, "MATCH(0,A1:A10000,0)").Value.Error);
        var before = session.GetStatistics();
        Assert.Equal(1, Evaluate(session, "MATCH(\"item*\",A1:A10000,0)").Value.Number);
        var after = session.GetStatistics();
        Assert.True(after.EvaluationSteps - before.EvaluationSteps < 100);
        Assert.Equal(before.SourceReads, after.SourceReads);
        Assert.Equal(before.RangeCandidates, after.RangeCandidates);
    }

    [Fact]
    public void DifferentExactKeysShareOneIncrementalIndex()
    {
        const int count = 30000;
        var builder = new InMemoryFormulaWorkbook.Builder().AddSheet("S");
        for (int i = 1; i <= count; i++) builder.SetValue(new("S", i, 1), FormulaValue.FromNumber(i));
        var session = new FormulaEngine().CreateSession(builder.Build());
        Assert.Equal(count, Evaluate(session, $"MATCH({count},A:A,0)").Value.Number);
        for (int i = 1; i <= 100; i++) Assert.Equal(i * 137, Evaluate(session, $"MATCH({i * 137},A:A,0)").Value.Number);
        var statistics = session.GetStatistics();
        Assert.Equal(count, statistics.RangeCandidates);
        Assert.Equal(count, statistics.SourceReads);
        Assert.Equal(count, statistics.LookupIndexedValues);
        Assert.Equal(1, statistics.LookupIndexedRanges);
        Assert.Equal(count + 1, Evaluate(session, "MATCH(0,A:A,0)").Value.Number);
        Assert.Equal(count, session.GetStatistics().SourceReads);
    }

    [Fact]
    public void IndexBuildStopsAtFirstMatchAndDoesNotHideLaterEngineFailure()
    {
        var source = new InMemoryFormulaWorkbook.Builder().AddSheet("S")
            .SetValue(new("S", 1, 1), FormulaValue.FromNumber(1))
            .SetFormula(new("S", 2, 1), "UNKNOWN()")
            .SetValue(new("S", 3, 1), FormulaValue.FromNumber(3)).Build();
        var session = new FormulaEngine().CreateSession(source);
        Assert.Equal(1, Evaluate(session, "MATCH(1,A1:A3,0)").Value.Number);
        Assert.Equal(1, session.GetStatistics().SourceReads);
        Assert.Equal("UnsupportedFunction", Evaluate(session, "MATCH(3,A1:A3,0)").Diagnostics[0].Code);
        Assert.Equal(1, Evaluate(session, "MATCH(1,A1:A3,0)").Value.Number);
        Assert.Equal("UnsupportedFunction", Evaluate(session, "MATCH(3,A1:A3,0)").Diagnostics[0].Code);
    }

    [Fact]
    public void CachedPrefixPreservesTypedDuplicatesWildcardOrderAndErrors()
    {
        var source = new InMemoryFormulaWorkbook.Builder().AddSheet("S")
            .SetValue(new("S", 1, 1), FormulaValue.FromText("alpha"))
            .SetValue(new("S", 2, 1), FormulaValue.FromText("ALPHA"))
            .SetValue(new("S", 3, 1), FormulaValue.FromText("2"))
            .SetValue(new("S", 4, 1), FormulaValue.FromNumber(2))
            .SetValue(new("S", 5, 1), FormulaValue.FromText("*"))
            .SetValue(new("S", 6, 1), FormulaValue.FromError("#N/A"))
            .SetValue(new("S", 7, 1), FormulaValue.FromNumber(7)).Build();
        var formulas = new[] { "MATCH(99,A1:A7,0)", "MATCH(\"a*\",A1:A7,0)", "MATCH(\"ALPHA\",A1:A7,0)", "MATCH(2,A1:A7,0)", "MATCH(\"2\",A1:A7,0)", "MATCH(\"~*\",A1:A7,0)", "MATCH(7,A1:A7,0)" };
        foreach (var data in new IFormulaDataSource[] { source, new DenseFormulaSource(source) })
        {
            var session = new FormulaEngine().CreateSession(data);
            foreach (var formula in formulas)
            {
                var fresh = Evaluate(new FormulaEngine().CreateSession(data), formula);
                var cached = Evaluate(session, formula);
                Assert.Equal(fresh.Success, cached.Success);
                Assert.Equal(fresh.Value, cached.Value);
            }
        }
    }

    [Fact]
    public void ValueLimitUsesStreamingFallbackWithoutChangingResults()
    {
        const int count = 66000;
        var builder = new InMemoryFormulaWorkbook.Builder().AddSheet("S");
        for (int i = 1; i <= count; i++) builder.SetValue(new("S", i, 1), FormulaValue.FromNumber(i));
        var session = new FormulaEngine().CreateSession(builder.Build());
        Assert.Equal(count, Evaluate(session, $"MATCH({count},A:A,0)").Value.Number);
        Assert.Equal(1, Evaluate(session, "MATCH(1,A:A,0)").Value.Number);
        Assert.Equal(65536, session.GetStatistics().LookupIndexedValues);
        Assert.Equal(count + 1, session.GetStatistics().RangeCandidates);
    }

    [Fact]
    public void TextAndRangeLimitsBoundRetainedIndexes()
    {
        var builder = new InMemoryFormulaWorkbook.Builder().AddSheet("S");
        for (int i = 1; i <= 5; i++) builder.SetValue(new("S", i, 1), FormulaValue.FromText(new string((char)('a' + i), 32767)));
        var session = new FormulaEngine().CreateSession(builder.Build());
        Assert.Equal("#N/A", Evaluate(session, "MATCH(9,A1:A5,0)").Value.Error);
        Assert.Equal(4, session.GetStatistics().LookupIndexedValues);
        builder = new InMemoryFormulaWorkbook.Builder().AddSheet("S");
        for (int i = 1; i <= 20; i++) builder.SetValue(new("S", i, 1), FormulaValue.FromNumber(1));
        session = new FormulaEngine().CreateSession(builder.Build());
        for (int i = 1; i <= 20; i++) Assert.Equal(1, Evaluate(session, $"MATCH(1,A{i}:B{i},0)").Value.Number);
        Assert.Equal(16, session.GetStatistics().LookupIndexedRanges);
    }

    [Fact]
    public void ReturningAnIndexedMatchStillReadsTheRequestedReturnColumn()
    {
        var source = new InMemoryFormulaWorkbook.Builder().AddSheet("S")
            .SetValue(new("S", 1, 1), FormulaValue.FromNumber(1))
            .SetValue(new("S", 1, 2), FormulaValue.FromNumber(10))
            .SetFormula(new("S", 1, 3), "UNKNOWN()").Build();
        var session = new FormulaEngine().CreateSession(source);
        Assert.Equal(10, Evaluate(session, "VLOOKUP(1,A1:C1,2,FALSE)").Value.Number);
        Assert.Equal("UnsupportedFunction", Evaluate(session, "VLOOKUP(1,A1:C1,3,FALSE)").Diagnostics[0].Code);
    }

    private static FormulaEvaluationResult Evaluate(FormulaSession session, string formula) => session.Evaluate(FormulaParser.Parse(formula).Formula!, new("S", 1, 26));
}
