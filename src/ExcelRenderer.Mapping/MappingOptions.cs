using System.Globalization;

namespace ExcelRenderer.Mapping;

/// <summary>Controls formatting and the maximum expanded worksheet size.</summary>
public sealed class MappingOptions
{
    /// <summary>Gets or sets the default formatting culture.</summary>
    public CultureInfo Culture { get; set; } = CultureInfo.InvariantCulture;

    /// <summary>Gets or sets the maximum number of output rows per worksheet.</summary>
    public int MaxOutputRows { get; set; } = 100_000;
}
