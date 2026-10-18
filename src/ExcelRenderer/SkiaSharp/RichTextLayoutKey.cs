using ExcelRenderer.Model;

namespace ExcelRenderer.SkiaSharp;

/// <summary>Compares immutable partial formatting structurally, including all font properties.</summary>
internal sealed class RichTextLayoutKey : IEquatable<RichTextLayoutKey>
{
    private readonly CellTextRun[] runs;
    private readonly FontStyle font;
    private readonly double width;
    private readonly bool wrap;
    private readonly int generation;

    /// <summary>Initializes a new instance of the <see cref="RichTextLayoutKey"/> class.</summary>
    /// <param name="runs">The owned immutable snapshot.</param>
    /// <param name="font">The empty-line font.</param>
    /// <param name="width">The available width.</param>
    /// <param name="wrap">The wrapping policy.</param>
    /// <param name="generation">The font registration generation.</param>
    internal RichTextLayoutKey(CellTextRun[] runs, FontStyle font, double width, bool wrap, int generation)
    {
        this.runs = runs;
        this.font = font;
        this.width = width;
        this.wrap = wrap;
        this.generation = generation;
    }

    /// <inheritdoc/>
    public bool Equals(RichTextLayoutKey? other) => other is not null && font == other.font && width == other.width &&
        wrap == other.wrap && generation == other.generation && runs.SequenceEqual(other.runs);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is RichTextLayoutKey other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        HashCode hash = default;
        hash.Add(font);
        hash.Add(width);
        hash.Add(wrap);
        hash.Add(generation);
        foreach (var run in runs)
        {
            hash.Add(run);
        }

        return hash.ToHashCode();
    }
}
