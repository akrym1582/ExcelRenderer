namespace ExcelRenderer.Model;

/// <summary>One original cell hyperlink definition, kept separately from cell display text.</summary>
/// <param name="SourceRange">The original inclusive source range.</param>
/// <param name="Target">The original relationship URI or internal location.</param>
/// <param name="IsExternal">Whether the definition refers to an external relationship.</param>
/// <param name="Location">An optional location accompanying the relationship.</param>
/// <param name="Tooltip">The original optional tooltip.</param>
/// <param name="DefinitionId">The worksheet definition identifier.</param>
public sealed record ReportHyperlink(
    CellRange SourceRange,
    string Target,
    bool IsExternal,
    string? Location = null,
    string? Tooltip = null,
    string? DefinitionId = null)
{
    /// <summary>Gets a deferred diagnostic code for an invalid original definition.</summary>
    internal string? IssueCode { get; init; }

    /// <summary>Gets a reason without including the hyperlink target.</summary>
    internal string? IssueReason { get; init; }
}
