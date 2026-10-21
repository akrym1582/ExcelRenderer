namespace ExcelRenderer.CoreIntegration;

/// <summary>Distinguishes proven absence from an exhausted bounded target search.</summary>
internal enum TargetSearchState
{
    /// <summary>A visible output target was found.</summary>
    Found,

    /// <summary>No affected target exists.</summary>
    Absent,

    /// <summary>The bounded search cannot prove absence.</summary>
    Indeterminate,
}
