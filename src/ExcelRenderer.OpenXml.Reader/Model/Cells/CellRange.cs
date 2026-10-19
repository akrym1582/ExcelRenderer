using System.IO;

namespace ExcelRenderer.OpenXml.Reader.Model.Cells;

/// <summary>A rectangular cell range with inclusive endpoints.</summary>
public sealed class CellRange
{
    /// <summary>Initializes a new instance of the <see cref="CellRange"/> class.</summary>
    /// <param name="first">The first value.</param>
    /// <param name="last">The last value.</param>
    public CellRange(CellAddress first, CellAddress last)
    {
        if (first.Row < 1 || first.Column < 1 || last.Row < first.Row || last.Column < first.Column)
        {
            throw new InvalidDataException("Reversed cell range.");
        }

        First = first;
        Last = last;
    }

    /// <summary>Gets the First metadata.</summary>
    public CellAddress First { get; }

    /// <summary>Gets the Last metadata.</summary>
    public CellAddress Last { get; }

    /// <summary>Parses an A1 rectangle, accepting absolute coordinates.</summary>
    /// <returns>The rectangle.</returns>
    /// <param name="text">The text value.</param>
    public static CellRange Parse(string text)
    {
        var parts = text.Split(':');
        if (parts.Length > 2)
        {
            throw new InvalidDataException("Invalid cell range.");
        }

        return new CellRange(CellAddress.ParseCore(parts[0], true), CellAddress.ParseCore(parts[parts.Length - 1], true));
    }

    /// <inheritdoc/>
    public override string ToString() => First + ":" + Last;

    /// <summary>Provides Contains metadata.</summary>
    /// <returns>The resulting value.</returns>
    /// <param name="address">The address value.</param>
    internal bool Contains(CellAddress address) => address.Row >= First.Row && address.Row <= Last.Row && address.Column >= First.Column && address.Column <= Last.Column;

    /// <summary>Provides Intersects metadata.</summary>
    /// <returns>The resulting value.</returns>
    /// <param name="other">The other value.</param>
    internal bool Intersects(CellRange other) => First.Row <= other.Last.Row && Last.Row >= other.First.Row && First.Column <= other.Last.Column && Last.Column >= other.First.Column;
}
