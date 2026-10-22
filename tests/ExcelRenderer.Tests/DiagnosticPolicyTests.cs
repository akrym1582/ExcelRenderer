using ExcelRenderer.Rendering;
using Xunit;

namespace ExcelRenderer.Tests;

public sealed class DiagnosticPolicyTests
{
    [Fact]
    public void Failure_precedes_suppression_and_limits_and_info_can_be_promoted()
    {
        var collector = new DiagnosticCollector(new() { StrictMode = true, SuppressedCodes = ["warning"], MaxDiagnostics = 1 });
        collector.Add(new("info", DiagnosticSeverity.Info, DiagnosticStage.Read, ""));
        Assert.False(collector.HasFailure);
        collector.Add(new("warning", DiagnosticSeverity.Warning, DiagnosticStage.Read, ""));
        Assert.True(collector.HasFailure);
        Assert.Equal(new[] { "info", "DiagnosticsTruncated" }, collector.ToArray().Select(d => d.Code));
        var promoted = new DiagnosticCollector(new() { TreatAsErrors = ["info"], SuppressedCodes = ["info"] });
        promoted.Add(new("info", DiagnosticSeverity.Info, DiagnosticStage.Read, ""));
        Assert.True(promoted.HasFailure);
        Assert.Empty(promoted.ToArray());
    }

    [Fact]
    public void Duplicate_identity_merges_occurrences_even_at_limit_and_new_identities_are_truncated()
    {
        var collector = new DiagnosticCollector(new() { MaxDiagnostics = 1 });
        var diagnostic = new ConversionDiagnostic("same", DiagnosticSeverity.Warning, DiagnosticStage.Read, "", SheetName: "data", CellRange: "A1");
        collector.Add(diagnostic);
        collector.Add(diagnostic with { OccurrenceCount = 3 });
        collector.Add(diagnostic with { CellRange = "A2" });
        var result = collector.ToArray();
        Assert.Equal(4, result[0].OccurrenceCount);
        Assert.Equal("DiagnosticsTruncated", result[1].Code);
        Assert.Equal(1, result[1].OccurrenceCount);
    }
}
