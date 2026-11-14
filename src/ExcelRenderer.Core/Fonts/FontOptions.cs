namespace ExcelRenderer.Core.Fonts;

/// <summary>Explicit drawing fonts and cell-family substitutions; no operating-system font discovery.</summary>
public sealed class FontOptions
{
    /// <summary>Gets static TrueType files, or collections using face zero, in preference order.</summary>
    public IReadOnlyList<string> Files { get; init; } = [];

    /// <summary>Gets direct source-to-supplied-family substitutions, matched without case sensitivity.</summary>
    public IDictionary<string, string> FamilyAliases { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the supplied family for missing Swiss (sans-serif) source fonts.</summary>
    public string? SansSerifFallback { get; init; }

    /// <summary>Gets the supplied family for missing Roman (serif) source fonts.</summary>
    public string? SerifFallback { get; init; }

    /// <summary>Gets the supplied family used when neither source nor classified fallback is available.</summary>
    public string? DefaultFamily { get; init; }
}
