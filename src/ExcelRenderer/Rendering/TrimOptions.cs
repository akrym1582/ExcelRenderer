namespace ExcelRenderer.Rendering;

/// <summary>Controls cropping around visible drawing content without changing layout.</summary>
public sealed record TrimOptions
{
    /// <summary>Gets a value indicating whether content cropping is enabled.</summary>
    public bool Enabled { get; init; }

    /// <summary>Gets the nonnegative padding in points on each side.</summary>
    public double PaddingPoints { get; init; } = 2;
}
