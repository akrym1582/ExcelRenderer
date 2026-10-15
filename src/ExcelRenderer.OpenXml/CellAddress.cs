using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace ExcelRenderer.OpenXml;

/// <summary>A validated one-based XLSX cell coordinate.</summary>
public readonly struct CellAddress : IEquatable<CellAddress>
{
    /// <summary>Initializes a new instance of the <see cref="CellAddress"/> struct.</summary>
    /// <param name="row">The row value.</param>
    /// <param name="column">The column value.</param>
    public CellAddress(int row, int column)
    {
        if (row < 1 || row > 1048576 || column < 1 || column > 16384)
        {
            throw new ArgumentOutOfRangeException(nameof(row), "Cell coordinate is outside the XLSX grid.");
        }

        Row = row;
        Column = column;
    }

    /// <summary>Gets the Row metadata.</summary>
    public int Row { get; }

    /// <summary>Gets the Column metadata.</summary>
    public int Column { get; }

    /// <summary>Parses a plain cell coordinate.</summary>
    /// <returns>The coordinate.</returns>
    /// <param name="text">The text value.</param>
    public static CellAddress Parse(string text) => ParseCore(text, false);

    /// <inheritdoc/>
    /// <param name="other">The other value.</param>
    public bool Equals(CellAddress other) => Row == other.Row && Column == other.Column;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is CellAddress other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => (Row * 397) ^ Column;

    /// <inheritdoc/>
    public override string ToString()
    {
        var col = Column;
        var name = string.Empty;
        while (col > 0)
        {
            col--;
            name = (char)('A' + (col % 26)) + name;
            col /= 26;
        }

        return name + Row.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Validates a cell coordinate with the specified absolute-reference policy.</summary>
    /// <returns>The resulting value.</returns>
    /// <param name="text">The text value.</param>
    /// <param name="allowAbsolute">The allowAbsolute value.</param>
    internal static CellAddress ParseCore(string text, bool allowAbsolute)
    {
        if (!Regex.IsMatch(text, allowAbsolute ? @"^\$?[A-Z]{1,3}\$?[1-9][0-9]{0,6}$" : @"^[A-Z]{1,3}[1-9][0-9]{0,6}$"))
        {
            throw new InvalidDataException("Invalid A1 coordinate.");
        }

        var plain = text.Replace("$", string.Empty);
        var i = 0;
        var col = 0;
        while (i < plain.Length && plain[i] >= 'A' && plain[i] <= 'Z')
        {
            col = checked((col * 26) + plain[i++] - 'A' + 1);
        }

        var row = int.Parse(plain.Substring(i), CultureInfo.InvariantCulture);
        if (row > 1048576 || col > 16384)
        {
            throw new InvalidDataException("Cell coordinate is outside the XLSX grid.");
        }

        return new CellAddress(row, col);
    }
}
