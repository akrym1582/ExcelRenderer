using ExcelRenderer.OpenXml.References;
using ExcelRenderer.OpenXml.Writing.Plans;
using Xunit;

namespace ExcelRenderer.OpenXml.Tests;

public sealed class StructuralReferenceIndexTests
{
    [Fact]
    public void DistinctIntervalsResolveNonMonotonicOccurrencesAndImplicitTrailingRows()
    {
        var rows = new List<OutputRowOccurrence>();
        for (var context = 1; context <= 3; context++)
        for (var source = 2; source <= 200; source++)
        {
            if ((source + context) % 7 != 0)
                rows.Add(new OutputRowOccurrence(source, rows.Count + 1, context, 0, []));
        }
        var plan = new SheetEditPlan(1, 200, rows, [], [], false);
        var references = new StructuralReferences(new Dictionary<string, SheetEditPlan> { ["Data"] = plan });
        var random = new Random(42);
        for (var sample = 0; sample < 500; sample++)
        {
            var first = random.Next(1, 230);
            var last = random.Next(first, 240);
            var outputRows = rows.Where(row => row.SourceRow >= first && row.SourceRow <= last).Select(row => row.OutputRow).ToList();
            if (last > 200)
            {
                outputRows.Add(Math.Max(first, 201) + rows.Count - 200);
                outputRows.Add(last + rows.Count - 200);
            }
            var expected = outputRows.Count == 0 ? "#REF!" : $"A{outputRows.Min()}:A{outputRows.Max()}";
            Assert.Equal(expected, references.Rewrite($"A{first}:A{last}", "Data"));
        }
        Assert.Equal("#REF!", references.Rewrite("A1:A1", "Data"));
    }
    [Fact]
    public void EmptyPlansKeepOnlyImplicitSurvivingRows()
    {
        var empty = new SheetEditPlan(1, 0, [], [], [], false);
        var deleted = new SheetEditPlan(2, 3, [], [], [], false);
        var references = new StructuralReferences(new Dictionary<string, SheetEditPlan> { ["Empty"] = empty, ["Deleted"] = deleted });
        Assert.Equal("A1:A2", references.Rewrite("A1:A2", "Empty"));
        Assert.Equal("#REF!", references.Rewrite("A1:A3", "Deleted"));
        Assert.Equal("A1:A2", references.Rewrite("A2:A5", "Deleted"));
    }

}
