using ExcelRenderer.Formulas.Data;
using ExcelRenderer.Formulas.Evaluation;
using ExcelRenderer.Formulas.References;
using ExcelRenderer.Formulas.Syntax;
using ExcelRenderer.Formulas.Values;
using Xunit;

namespace ExcelRenderer.Formulas.Tests;

public sealed class SparseRangeTests
{
    [Theory]
    [InlineData(10)]
    [InlineData(1000)]
    [InlineData(100000)]
    public void WholeColumnVisitsOnlyStoredValues(int count)
    {
        var builder = new InMemoryFormulaWorkbook.Builder().AddSheet("Data").AddSheet("Report");
        for (int row = 1; row <= count; row++) builder.SetValue(new("Data", row == count ? 1048576 : row, 1), FormulaValue.FromNumber(2));
        builder.SetFormula(new("Report", 1, 1), "SUM(Data!A:A)");
        var session = new FormulaEngine().CreateSession(builder.Build());
        var result = session.EvaluateCell(new("Report", 1, 1));
        Assert.True(result.Success);
        Assert.Equal(count * 2, result.Value.Number);
        Assert.Equal(count + 1, session.GetStatistics().SourceReads);
        Assert.Equal(count + 1, session.GetStatistics().CachedValues);
        Assert.Equal(count, session.GetStatistics().RangeCandidates);
    }

    [Fact]
    public void IndexedDensityNeverTreatsHolesAsStoredCells()
    {
        var source = new InMemoryFormulaWorkbook.Builder().AddSheet("S")
            .SetValue(new("S", 1, 1), FormulaValue.FromNumber(1))
            .SetValue(new("S", 3, 1), FormulaValue.FromNumber(3)).Build();
        var session = new FormulaEngine().CreateSession(source, new() { MaxCellVisits = 2 });
        var result = session.Evaluate(FormulaParser.Parse("SUM(A1:A3)").Formula!, new("S", 1, 2));
        Assert.True(result.Success);
        Assert.Equal(4, result.Value.Number);
        Assert.Equal(2, session.GetStatistics().CellVisits);
    }

    [Fact]
    public void IndexedQueryForTheWrongRangeIsStillValidated()
    {
        var result = new FormulaEngine().Evaluate(FormulaParser.Parse("SUM(A1:A1)").Formula!, new WrongIndexedQuerySource(), new("S", 1, 3));
        Assert.False(result.Success);
        Assert.Equal("DataSourceFailure", result.Diagnostics[0].Code);
    }

    [Fact]
    public void RangeAdvancementChargesEachCoordinateWithoutDoubleChargingItsRead()
    {
        var builder = new InMemoryFormulaWorkbook.Builder().AddSheet("S");
        for (int i = 1; i <= 100; i++) builder.SetValue(new("S", i, 1), FormulaValue.FromNumber(1));
        var source = builder.Build();
        foreach (var data in new IFormulaDataSource[] { source, new DenseFormulaSource(source) })
        {
            var parsed = FormulaParser.Parse("SUM(A1:A100)").Formula!;
            var result = new FormulaEngine().Evaluate(parsed, data, new("S", 1, 2), new() { MaxEvaluationSteps = 250 });
            Assert.True(result.Success);
            Assert.Equal(100, result.Value.Number);
            result = new FormulaEngine().Evaluate(parsed, data, new("S", 1, 2), new() { MaxEvaluationSteps = 100 });
            Assert.Equal("ResourceLimitExceeded", result.Diagnostics[0].Code);
        }
    }

    [Fact]
    public void FullSheetBlanksUseLongArithmeticAndNoValueCache()
    {
        var session = new FormulaEngine().CreateSession(new InMemoryFormulaWorkbook.Builder().AddSheet("S").Build());
        var result = session.Evaluate(FormulaParser.Parse("COUNTIF(A:XFD,\"\")").Formula!, new("S", 1, 1));
        Assert.True(result.Success);
        Assert.Equal(17179869184d, result.Value.Number);
        Assert.Equal(17179869184L, session.GetStatistics().BulkBlankCells);
        Assert.Equal(0, session.GetStatistics().SourceReads);
        Assert.Equal(0, session.GetStatistics().CachedValues);
    }

    [Fact]
    public void SparseWholeColumnStillDetectsSelfReference()
    {
        var source = new InMemoryFormulaWorkbook.Builder().AddSheet("S").SetFormula(new("S", 40, 1), "SUM(A:A)").Build();
        Assert.Equal("CircularReference", new FormulaEngine().EvaluateCell(new("S", 40, 1), source).Diagnostics[0].Code);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("reverse")]
    [InlineData("outside")]
    [InlineData("throw")]
    public void InvalidSparseContractsProduceDiagnostics(string mode)
    {
        var result = new FormulaEngine().Evaluate(FormulaParser.Parse("SUM(A1:A3)").Formula!, new MalformedSource(mode), new("S", 1, 2));
        Assert.False(result.Success);
        Assert.Equal("DataSourceFailure", result.Diagnostics[0].Code);
    }

    [Fact]
    public void EmptyDenseSourceIsStillBoundedAndDoesNotCacheBlanks()
    {
        var source = new DenseFormulaSource(new InMemoryFormulaWorkbook.Builder().AddSheet("S").Build());
        var session = new FormulaEngine().CreateSession(source, new() { MaxCellVisits = 100 });
        var result = session.Evaluate(FormulaParser.Parse("SUM(A:A)").Formula!, new("S", 1, 2));
        Assert.Equal("ResourceLimitExceeded", result.Diagnostics[0].Code);
        Assert.Equal(0, session.GetStatistics().CachedValues);
    }

    [Fact]
    public void GeneratedDenseAndSparseEvaluationAgree()
    {
        var random = new Random(1582);
        var expressions = new[] { "SUM(A2:C5)", "COUNT(A2:C5)", "COUNTA(A2:C5)", "MIN(A2:C5)", "MAX(A2:C5)", "AVERAGE(A2:C5)", "AND(A2:C5)", "OR(A2:C5)", "COUNTIF(A2:C5,0)", "COUNTIF(A2:C5,\"\")", "COUNTIFS(A2:C5,0,E8:G11,\"<>2\")", "COUNTIFS(A2:C5,\"\",E8:G11,\"\")", "SUMIF(A2:C5,\"<>2\",J2)", "SUMIFS(J2:L5,A2:C5,\"\",E8:G11,0)" };
        for (int iteration = 0; iteration < 100; iteration++)
        {
            var builder = new InMemoryFormulaWorkbook.Builder().AddSheet("S");
            foreach (var (startRow, startColumn) in new[] { (2, 1), (8, 5), (2, 10) })
            {
                for (int row = 0; row < 4; row++)
                for (int col = 0; col < 3; col++)
                {
                    var address = new FormulaCellAddress("S", startRow + row, startColumn + col);
                    switch (random.Next(9))
                    {
                        case 0: case 1: break;
                        case 2: builder.SetValue(address, FormulaValue.FromNumber(0)); break;
                        case 3: builder.SetValue(address, FormulaValue.FromNumber(2)); break;
                        case 4: builder.SetValue(address, FormulaValue.FromText("2")); break;
                        case 5: builder.SetValue(address, FormulaValue.FromBoolean(true)); break;
                        case 6: builder.SetFormula(address, "\"\""); break;
                        case 7: builder.SetValue(address, FormulaValue.FromError("#N/A")); break;
                        case 8: builder.SetFormula(address, "1+2"); break;
                    }
                }
            }

            var source = builder.Build();
            foreach (var expression in expressions)
            {
                var parsed = FormulaParser.Parse(expression).Formula!;
                var engine = new FormulaEngine();
                var sparse = engine.Evaluate(parsed, source, new("S", 1, 26));
                var dense = engine.Evaluate(parsed, new DenseFormulaSource(source), new("S", 1, 26));
                Assert.Equal(dense.Success, sparse.Success);
                Assert.Equal(dense.Value, sparse.Value);
                Assert.Equal(dense.Diagnostics.Select(d => d.Code), sparse.Diagnostics.Select(d => d.Code));
            }
        }
    }

    [Fact]
    public void LaterCriteriaEngineFailuresAreNotHiddenByEarlierMismatch()
    {
        var source = new InMemoryFormulaWorkbook.Builder().AddSheet("S")
            .SetValue(new("S", 1, 1), FormulaValue.FromNumber(1))
            .SetFormula(new("S", 1, 2), "UNKNOWN()")
            .SetFormula(new("S", 2, 2), "B2").Build();
        foreach (var data in new IFormulaDataSource[] { source, new DenseFormulaSource(source) })
        {
            var parsed = FormulaParser.Parse("COUNTIFS(A1,2,B1,0)").Formula!;
            Assert.Equal("UnsupportedFunction", new FormulaEngine().Evaluate(parsed, data, new("S", 1, 26)).Diagnostics[0].Code);
            parsed = FormulaParser.Parse("COUNTIFS(A2,2,B2,0)").Formula!;
            Assert.Equal("CircularReference", new FormulaEngine().Evaluate(parsed, data, new("S", 1, 26)).Diagnostics[0].Code);
        }
    }

    private sealed class WrongIndexedQuerySource : ISparseFormulaDataSource
    {
        private readonly FormulaRangeIndex index = new(new[] { new FormulaCellAddress("S", 1, 1), new FormulaCellAddress("S", 1, 2) });
        public IEnumerable<FormulaCellAddress> EnumerateStoredCells(FormulaRange range, CancellationToken cancellationToken) => index.Enumerate(new("S", 1, 2, 1, 2), cancellationToken);
        public FormulaCellData GetCell(FormulaCellAddress address) => FormulaCellData.FromValue(FormulaValue.FromNumber(1));
        public bool TryResolveSheet(string name, out string id) { id = name; return true; }
        public bool TryResolveName(string? sheet, string name, string current, out FormulaRange range) { range = default; return false; }
    }

    private sealed class MalformedSource(string mode) : ISparseFormulaDataSource
    {
        public IEnumerable<FormulaCellAddress> EnumerateStoredCells(FormulaRange range, CancellationToken cancellationToken)
        {
            if (mode == "throw") throw new IOException("enumeration failed");
            yield return new("S", 2, 1);
            yield return new("S", mode == "duplicate" ? 2 : mode == "reverse" ? 1 : 4, 1);
        }
        public FormulaCellData GetCell(FormulaCellAddress address) => FormulaCellData.FromValue(FormulaValue.FromNumber(1));
        public bool TryResolveSheet(string name, out string id) { id = name; return true; }
        public bool TryResolveName(string? sheet, string name, string current, out FormulaRange range) { range = default; return false; }
    }
}

internal sealed class DenseFormulaSource(IFormulaDataSource source) : IFormulaDataSource
{
    public FormulaCellData GetCell(FormulaCellAddress address) => source.GetCell(address);
    public bool TryResolveSheet(string name, out string id) => source.TryResolveSheet(name, out id);
    public bool TryResolveName(string? sheet, string name, string current, out FormulaRange range) => source.TryResolveName(sheet, name, current, out range);
}
