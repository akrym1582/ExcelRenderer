using ExcelRenderer.Formulas.Data;
using ExcelRenderer.Formulas.Evaluation;
using ExcelRenderer.Formulas.References;
using ExcelRenderer.Formulas.Syntax;
using ExcelRenderer.Formulas.Values;
using Xunit;

namespace ExcelRenderer.Formulas.Tests;

public sealed class RangeIndexWorkTests
{
    [Theory]
    [InlineData(10, 0)]
    [InlineData(2000, 0)]
    [InlineData(10, 1)]
    [InlineData(2000, 1)]
    [InlineData(10, 3)]
    [InlineData(2000, 3)]
    public void SingleColumnWorkIsIndependentOfUnrelatedRows(int unrelated, int stored)
    {
        var builder = new InMemoryFormulaWorkbook.Builder().AddSheet("S");
        for (int row = 1; row <= unrelated; row++) builder.SetValue(new("S", row, 1), FormulaValue.FromNumber(row));
        foreach (var row in new[] { 500000, 1, 1048576 }.Take(stored)) builder.SetValue(new("S", row, 2), FormulaValue.FromNumber(7));
        var workbook = builder.Build();
        foreach (var (formula, expected) in new[] { ("COUNTIF(B:B,\"\")", 1048576d - stored), ("SUM(B:B)", 7d * stored) })
        {
            var session = new FormulaEngine().CreateSession(workbook);
            var result = session.Evaluate(FormulaParser.Parse(formula).Formula!, new("S", 1, 26));
            Assert.True(result.Success);
            Assert.Equal(expected, result.Value.Number);
            var stats = session.GetStatistics();
            Assert.Equal(stored, stats.SourceReads);
            Assert.Equal(stored, stats.RangeCandidates);
            // Two coverage probes, two enumeration probes, ceil(log2(N+1)) comparisons, and K coordinates.
            Assert.Equal(stored == 0 ? 4 : stored == 1 ? 6 : 9, stats.IndexWork);
        }
    }

    [Fact]
    public void LimitedColumnRangeIncludesBoundariesAndExcludesOutsideValues()
    {
        var index = new FormulaRangeIndex(new[] { 1, 4, 5, 7, 8, 1048576 }.Select(r => new FormulaCellAddress("S", r, 2)));
        Assert.Equal(new[] { 5, 7 }, index.Enumerate(new("s", 5, 2, 7, 2)).Select(c => c.Row));
        Assert.Empty(index.Enumerate(new("S", 9, 2, 10, 2)));
        Assert.Empty(index.Enumerate(new("missing", 1, 2, 1048576, 2)));
    }

    [Fact]
    public void MultiColumnCoordinatesRemainOrderedUniqueAndInRange()
    {
        var addresses = new[] { new FormulaCellAddress("S", 7, 2), new("s", 2, 3), new("S", 2, 2), new("S", 2, 2), new("S", 1, 1), new("S", 8, 4) };
        var index = new FormulaRangeIndex(addresses);
        Assert.Equal(new[] { (2, 2), (2, 3), (7, 2) }, index.Enumerate(new("s", 2, 2, 7, 3)).Select(a => (a.Row, a.Column)));
        Assert.Equal(new[] { 2, 7 }, index.Enumerate(new("s", 2, 2, 7, 2)).Select(a => a.Row));
    }

    [Fact]
    public void NonReturningBuiltInScanConsumesBudgetAndPreservesResourceDiagnostic()
    {
        var source = new IndexedSource(2000);
        var session = new FormulaEngine().CreateSession(source, new() { MaxEvaluationSteps = 80 });
        var result = session.Evaluate(FormulaParser.Parse("SUM(B:C)").Formula!, new("S", 1, 26));
        Assert.False(result.Success);
        Assert.Equal("ResourceLimitExceeded", result.Diagnostics[0].Code);
        Assert.Equal(0, session.GetStatistics().RangeCandidates);
        Assert.Equal(0, session.GetStatistics().SourceReads);
        Assert.InRange(session.GetStatistics().IndexWork, 50, 81);
        Assert.Equal(81, session.GetStatistics().EvaluationSteps);
    }

    [Fact]
    public void DensityProofAlsoConsumesBudgetBeforeReturningCandidates()
    {
        var index = new FormulaRangeIndex(Enumerable.Range(1, 100).SelectMany(c => new[] { new FormulaCellAddress("S", 1, c), new("S", 2, c) }));
        var session = new FormulaEngine().CreateSession(new IndexedSource(index), new() { MaxEvaluationSteps = 40 });
        var result = session.Evaluate(FormulaParser.Parse("SUM(A1:CV2)").Formula!, new("S", 3, 26));
        Assert.Equal("ResourceLimitExceeded", result.Diagnostics[0].Code);
        Assert.True(session.GetStatistics().IndexWork > 0);
        Assert.Equal(0, session.GetStatistics().RangeCandidates);
    }

    [Fact]
    public void CancellationInterruptsIndexInsideMoveNextBeforeAnyCandidate()
    {
        using var cancellation = new CancellationTokenSource();
        var index = new FormulaRangeIndex(Enumerable.Range(1, 2000).Select(r => new FormulaCellAddress("S", r, 1)));
        var query = (FormulaRangeIndex.Query)index.Enumerate(new("S", 1, 2, 1048576, 3), cancellation.Token);
        int work = 0;
        using var cursor = query.WithWork(() => { if (++work == 40) cancellation.Cancel(); }).GetEnumerator();
        Assert.Throws<OperationCanceledException>(() => cursor.MoveNext());
        Assert.Equal(40, work);
    }

    [Fact]
    public void SessionCancellationInterruptsNonReturningBuiltInScan()
    {
        using var cancellation = new CancellationTokenSource();
        var index = new FormulaRangeIndex(Enumerable.Range(1, 2000).Select(r => new FormulaCellAddress("S", r, 1)));
        int work = 0;
        var source = new IndexedSource(index, () => { if (++work == 40) cancellation.Cancel(); });
        var session = new FormulaEngine().CreateSession(source, cancellationToken: cancellation.Token);
        Assert.Throws<OperationCanceledException>(() => session.Evaluate(FormulaParser.Parse("SUM(B:C)").Formula!, new("S", 1, 26)));
        Assert.Equal(40, work);
        Assert.Equal(40, session.GetStatistics().IndexWork);
        Assert.Equal(0, session.GetStatistics().RangeCandidates);
        Assert.Equal(0, session.GetStatistics().SourceReads);
    }

    [Fact]
    public void ConstructionAndSessionRespectCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        IEnumerable<FormulaCellAddress> Addresses()
        {
            yield return new("S", 1, 1);
            cancellation.Cancel();
            yield return new("S", 2, 1);
        }
        Assert.Throws<OperationCanceledException>(() => new FormulaRangeIndex(Addresses(), cancellation.Token));
        var session = new FormulaEngine().CreateSession(new IndexedSource(2000), cancellationToken: cancellation.Token);
        Assert.Throws<OperationCanceledException>(() => session.Evaluate(FormulaParser.Parse("SUM(B:C)").Formula!, new("S", 1, 26)));
    }

    private sealed class IndexedSource : ISparseFormulaDataSource
    {
        private readonly FormulaRangeIndex index;
        private readonly Action? work;
        public IndexedSource(int count) : this(new FormulaRangeIndex(Enumerable.Range(1, count).Select(r => new FormulaCellAddress("S", r, 1)))) { }
        public IndexedSource(FormulaRangeIndex index, Action? work = null) { this.index = index; this.work = work; }
        public IEnumerable<FormulaCellAddress> EnumerateStoredCells(FormulaRange range, CancellationToken cancellationToken)
        {
            var query = (FormulaRangeIndex.Query)index.Enumerate(range, cancellationToken);
            return work == null ? query : query.WithWork(work);
        }
        public FormulaCellData GetCell(FormulaCellAddress address) => throw new InvalidOperationException("No candidate should be returned");
        public bool TryResolveSheet(string name, out string id) { id = name; return true; }
        public bool TryResolveName(string? sheet, string name, string current, out FormulaRange range) { range = default; return false; }
    }
}
