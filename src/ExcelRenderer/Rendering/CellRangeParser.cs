using System.Globalization;
using System.Text;
using ExcelRenderer.Model;

namespace ExcelRenderer.Rendering;

/// <summary>Parses strict Excel A1 rectangles and optional worksheet qualifiers.</summary>
public static class CellRangeParser
{
    /// <summary>Parses an A1 reference without expanding or evaluating it.</summary>
    /// <param name="text">A single cell or rectangle, optionally qualified by a sheet.</param>
    /// <param name="sheetName">The unescaped sheet name, or null for an unqualified reference.</param>
    /// <returns>The inclusive, one-based range.</returns>
    public static CellRange Parse(string text, out string? sheetName)
    {
        if (text is null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        text = text.Trim();
        sheetName = null;
        var separator = -1;
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\'')
            {
                if (quoted && i + 1 < text.Length && text[i + 1] == '\'')
                {
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (text[i] == '!' && !quoted)
            {
                if (separator >= 0)
                {
                    throw Invalid();
                }

                separator = i;
            }
        }

        if (quoted)
        {
            throw Invalid();
        }

        if (separator >= 0)
        {
            var name = text.Substring(0, separator);
            if (name.StartsWith("'", StringComparison.Ordinal))
            {
                if (name.Length < 2 || name[name.Length - 1] != '\'')
                {
                    throw Invalid();
                }

                var decoded = new StringBuilder();
                for (var i = 1; i < name.Length - 1; i++)
                {
                    if (name[i] == '\'' && (++i >= name.Length - 1 || name[i] != '\''))
                    {
                        throw Invalid();
                    }

                    decoded.Append(name[i]);
                }

                sheetName = decoded.ToString();
            }
            else
            {
                if (name.Any(c => char.IsWhiteSpace(c) || c == '\''))
                {
                    throw Invalid();
                }

                sheetName = name;
            }

            if (string.IsNullOrEmpty(sheetName) || sheetName!.IndexOfAny(['[', ']', ':']) >= 0)
            {
                throw Invalid();
            }

            text = text.Substring(separator + 1);
        }

        var position = 0;
        var first = ReadAddress(text, ref position);
        var last = first;
        if (position < text.Length && text[position] == ':')
        {
            position++;
            last = ReadAddress(text, ref position);
        }

        if (position != text.Length || first.Row > last.Row || first.Column > last.Column)
        {
            throw Invalid();
        }

        return new(first, last);
    }

    /// <summary>Validates the dimensions and ordering of an API range.</summary>
    /// <param name="range">The inclusive range.</param>
    /// <returns>The checked number of cells.</returns>
    public static long CountCells(CellRange range)
    {
        if (range.First.Row < 1 || range.First.Column < 1 || range.Last.Row > 1_048_576 ||
            range.Last.Column > 16_384 || range.Last.Row < range.First.Row || range.Last.Column < range.First.Column)
        {
            throw Invalid();
        }

        return checked(((long)range.Last.Row - range.First.Row + 1) * ((long)range.Last.Column - range.First.Column + 1));
    }

    private static ArgumentException Invalid() => new("Expected a single A1 cell or forward rectangle within A1:XFD1048576.");

    private static CellAddress ReadAddress(string text, ref int position)
    {
        if (position < text.Length && text[position] == '$')
        {
            position++;
        }

        var column = 0;
        var letters = 0;
        while (position < text.Length)
        {
            var c = text[position];
            if (c is >= 'a' and <= 'z')
            {
                c = (char)(c - 'a' + 'A');
            }

            if (c is < 'A' or > 'Z')
            {
                break;
            }

            if (++letters > 3)
            {
                throw Invalid();
            }

            column = (column * 26) + c - 'A' + 1;
            position++;
        }

        if (position < text.Length && text[position] == '$')
        {
            position++;
        }

        var start = position;
        while (position < text.Length && text[position] is >= '0' and <= '9')
        {
            position++;
        }

        if (letters == 0 || column > 16_384 || start == position ||
            !int.TryParse(text.Substring(start, position - start), NumberStyles.None, CultureInfo.InvariantCulture, out var row) ||
            row is < 1 or > 1_048_576)
        {
            throw Invalid();
        }

        return new(row, column);
    }
}
