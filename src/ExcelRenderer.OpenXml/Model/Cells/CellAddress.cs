using System;
using System.Globalization;
using System.IO;

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
        if (string.IsNullOrEmpty(text))
        {
            throw new InvalidDataException("Invalid A1 coordinate.");
        }

        var i = 0;
        if (allowAbsolute && text[i] == '$')
        {
            i++;
        }

        var columnStart = i;
        var column = 0;
        while (i < text.Length && text[i] >= 'A' && text[i] <= 'Z')
        {
            if (i - columnStart == 3)
            {
                throw new InvalidDataException("Invalid A1 coordinate.");
            }

            column = (column * 26) + text[i++] - 'A' + 1;
        }

        if (column == 0 || column > 16384)
        {
            throw new InvalidDataException("Cell coordinate is outside the XLSX grid.");
        }

        if (allowAbsolute && i < text.Length && text[i] == '$')
        {
            i++;
        }

        if (i == text.Length || text[i] < '1' || text[i] > '9')
        {
            throw new InvalidDataException("Invalid A1 coordinate.");
        }

        var row = 0;
        for (; i < text.Length; i++)
        {
            if (text[i] < '0' || text[i] > '9' || row > 1048576)
            {
                throw new InvalidDataException("Invalid A1 coordinate.");
            }

            row = (row * 10) + text[i] - '0';
        }

        if (row > 1048576)
        {
            throw new InvalidDataException("Cell coordinate is outside the XLSX grid.");
        }

        return new CellAddress(row, column);
    }
}
