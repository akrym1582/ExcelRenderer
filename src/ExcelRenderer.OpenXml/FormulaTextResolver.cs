using System;
using System.Globalization;
using System.Text;

namespace ExcelRenderer.OpenXml;

/// <summary>Expands saved shared-formula references without evaluating any formula.</summary>
public static class FormulaTextResolver
{
    /// <summary>Resolves a saved formula, translating relative A1 references in shared followers.</summary>
    /// <param name="cell">The original source snapshot.</param>
    /// <param name="text">Formula text without the equals sign, or null when unavailable.</param>
    /// <returns>Whether the saved text can be represented faithfully.</returns>
    public static bool TryResolve(CellRecord cell, out string? text)
    {
        if (cell == null)
        {
            throw new ArgumentNullException(nameof(cell));
        }

        var formula = cell.Formula;
        text = formula?.RawFormula;
        if (formula == null || text!.Length > 0)
        {
            return true;
        }

        if (formula.Kind != "shared" || formula.MasterAddress == null || formula.MasterFormula == null)
        {
            text = null;
            return false;
        }

        return TryTranslate(formula.MasterFormula, cell.Address.Row - formula.MasterAddress.Value.Row, cell.Address.Column - formula.MasterAddress.Value.Column, out text);
    }

    private static bool TryTranslate(string source, int rowDelta, int columnDelta, out string? text)
    {
        text = null;
        var result = new StringBuilder(source.Length);
        var index = 0;
        while (index < source.Length)
        {
            var start = index;
            var current = source[index];
            if (current is '"' or '\'')
            {
                var quote = current;
                index++;
                var closed = false;
                while (index < source.Length)
                {
                    if (source[index++] != quote)
                    {
                        continue;
                    }

                    if (index < source.Length && source[index] == quote)
                    {
                        index++;
                        continue;
                    }

                    closed = true;
                    break;
                }

                if (!closed || (quote == '\'' && (index == source.Length || source[index] != '!')))
                {
                    return false;
                }

                result.Append(source, start, index - start);
                continue;
            }

            if (current is '[' or ']' or '{' or '}' or '#')
            {
                // External/structured references, array literals and spill operators require a wider grammar.
                return false;
            }

            if (char.IsDigit(current) || (current == '$' && index + 1 < source.Length && char.IsDigit(source[index + 1])) || (current == '.' && index + 1 < source.Length && char.IsDigit(source[index + 1])))
            {
                var absolute = current == '$';
                index += absolute ? 1 : 0;
                while (index < source.Length && char.IsDigit(source[index]))
                {
                    index++;
                }

                var integerEnd = index;
                var before = start - 1;
                while (before >= 0 && char.IsWhiteSpace(source[before]))
                {
                    before--;
                }

                var after = index;
                while (after < source.Length && char.IsWhiteSpace(source[after]))
                {
                    after++;
                }

                if ((before >= 0 && source[before] == ':') || (after < source.Length && source[after] == ':'))
                {
                    if (!int.TryParse(source.Substring(start + (absolute ? 1 : 0), integerEnd - start - (absolute ? 1 : 0)), NumberStyles.None, CultureInfo.InvariantCulture, out var row))
                    {
                        return false;
                    }

                    row += absolute ? 0 : rowDelta;
                    if (row is < 1 or > 1048576)
                    {
                        return false;
                    }

                    result.Append(absolute ? "$" : string.Empty).Append(row.ToString(CultureInfo.InvariantCulture));
                    continue;
                }

                if (absolute)
                {
                    return false;
                }

                if (index < source.Length && source[index] == '.')
                {
                    index++;
                    while (index < source.Length && char.IsDigit(source[index]))
                    {
                        index++;
                    }
                }

                if (index < source.Length && source[index] is 'e' or 'E')
                {
                    index++;
                    if (index < source.Length && source[index] is '+' or '-')
                    {
                        index++;
                    }

                    var exponent = index;
                    while (index < source.Length && char.IsDigit(source[index]))
                    {
                        index++;
                    }

                    if (exponent == index)
                    {
                        return false;
                    }
                }

                result.Append(source, start, index - start);
                continue;
            }

            if (char.IsLetter(current) || current is '$' or '_')
            {
                index++;
                while (index < source.Length && (char.IsLetterOrDigit(source[index]) || source[index] is '$' or '_' or '.'))
                {
                    index++;
                }

                var token = source.Substring(start, index - start);
                var following = index;
                while (following < source.Length && char.IsWhiteSpace(source[following]))
                {
                    following++;
                }

                if (following < source.Length && source[following] is '(' or '!')
                {
                    result.Append(token);
                    continue;
                }

                if (following < source.Length && source[following] == ':')
                {
                    var qualifier = following + 1;
                    while (qualifier < source.Length && (char.IsLetterOrDigit(source[qualifier]) || source[qualifier] is '_' or '.' or '$' or '\''))
                    {
                        qualifier++;
                    }

                    if (qualifier < source.Length && source[qualifier] == '!')
                    {
                        return false;
                    }
                }

                var position = 0;
                var fixedColumn = token[position] == '$';
                position += fixedColumn ? 1 : 0;
                var column = 0;
                var letters = position;
                while (position < token.Length && token[position] is >= 'A' and <= 'Z' or >= 'a' and <= 'z')
                {
                    if (position - letters >= 3)
                    {
                        break;
                    }

                    column = (column * 26) + char.ToUpperInvariant(token[position++]) - 'A' + 1;
                }

                var beforeToken = start - 1;
                while (beforeToken >= 0 && char.IsWhiteSpace(source[beforeToken]))
                {
                    beforeToken--;
                }

                if (position == token.Length && column is >= 1 and <= 16384 &&
                    ((following < source.Length && source[following] == ':') || (beforeToken >= 0 && source[beforeToken] == ':')))
                {
                    var translated = column + (fixedColumn ? 0 : columnDelta);
                    if (translated is < 1 or > 16384)
                    {
                        return false;
                    }

                    var columnAddress = new CellAddress(1, translated).ToString();
                    result.Append(fixedColumn ? "$" : string.Empty).Append(columnAddress, 0, columnAddress.Length - 1);
                    continue;
                }

                var fixedRow = position < token.Length && token[position] == '$';
                position += fixedRow ? 1 : 0;
                if (column is < 1 or > 16384 || position == token.Length || !int.TryParse(token.Substring(position), NumberStyles.None, CultureInfo.InvariantCulture, out var row) || row is < 1 or > 1048576)
                {
                    if (token.IndexOf('$') >= 0)
                    {
                        return false;
                    }

                    result.Append(token);
                    continue;
                }

                row += fixedRow ? 0 : rowDelta;
                column += fixedColumn ? 0 : columnDelta;
                if (row is < 1 or > 1048576 || column is < 1 or > 16384)
                {
                    return false;
                }

                var address = new CellAddress(row, column).ToString();
                var digit = 0;
                while (digit < address.Length && char.IsLetter(address[digit]))
                {
                    digit++;
                }

                result.Append(fixedColumn ? "$" : string.Empty).Append(address, 0, digit)
                    .Append(fixedRow ? "$" : string.Empty).Append(address, digit, address.Length - digit);
                continue;
            }

            result.Append(current);
            index++;
        }

        text = result.ToString();
        return true;
    }
}
