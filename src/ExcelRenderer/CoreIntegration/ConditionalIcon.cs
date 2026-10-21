namespace ExcelRenderer.CoreIntegration;

/// <summary>Selected fixed vector icon.</summary>
internal sealed record ConditionalIcon(string Set, int Index, bool ShowValue, bool RightToLeft)
{
    /// <summary>Gets the winning source rule for aggregated layout diagnostics.</summary>
    internal string? RuleId { get; init; }
}
