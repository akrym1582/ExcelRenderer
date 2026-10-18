using System.Globalization;
using System.Text;

namespace ExcelRenderer.Core.Pdf.Structure;

/// <summary>Parses bounded tokens without scanning stream bytes for dictionary syntax.</summary>
internal sealed class PdfSyntaxReader
{
    private readonly Stream input;
    private int tokens;

    /// <summary>Initializes a new instance of the <see cref="PdfSyntaxReader"/> class.</summary>
    /// <param name="input">The borrowed seekable PDF stream.</param>
    internal PdfSyntaxReader(Stream input) => this.input = input;

    /// <summary>Gets or sets the byte position.</summary>
    internal long Position { get => input.Position; set => input.Position = value; }

    /// <summary>Reads a whitespace-delimited token.</summary>
    /// <returns>The token.</returns>
    internal string Token()
    {
        Skip();
        var result = new StringBuilder();
        while (input.Position < input.Length && !Delimiter(Peek()))
        {
            result.Append((char)Read());
            if (result.Length > 1048576)
            {
                throw new InvalidDataException("PDF token exceeds the generated-format limit.");
            }
        }

        if (result.Length == 0)
        {
            throw new InvalidDataException("Expected a PDF token.");
        }

        return result.ToString();
    }

    /// <summary>Reads a dictionary, array, name, string, literal, number or reference.</summary>
    /// <param name="depth">The current nesting depth.</param>
    /// <returns>The parsed value.</returns>
    internal PdfValue Value(int depth = 0)
    {
        if (depth > 64 || ++tokens > 2000000)
        {
            throw new InvalidDataException("PDF syntax exceeds generated-format limits.");
        }

        Skip();
        var first = Read();
        if (first == '<')
        {
            if (Peek() == '<')
            {
                Read();
                var entries = new Dictionary<string, PdfValue>(StringComparer.Ordinal);
                while (true)
                {
                    Skip();
                    if (Peek() == '>')
                    {
                        Read();
                        ExpectByte('>');
                        return new PdfMap(entries);
                    }

                    if (Value(depth + 1) is not PdfName name || entries.ContainsKey(name.Value))
                    {
                        throw new InvalidDataException("Invalid or duplicate PDF dictionary key.");
                    }

                    entries.Add(name.Value, Value(depth + 1));
                }
            }

            return ReadString('<', '>');
        }

        if (first == '(')
        {
            return ReadString('(', ')');
        }

        if (first == '[')
        {
            var items = new List<PdfValue>();
            while (true)
            {
                Skip();
                if (Peek() == ']')
                {
                    Read();
                    return new PdfArray(items);
                }

                items.Add(Value(depth + 1));
            }
        }

        if (first == '/')
        {
            var name = new StringBuilder("/");
            while (input.Position < input.Length && !Delimiter(Peek()))
            {
                name.Append((char)Read());
                if (name.Length > 4096)
                {
                    throw new InvalidDataException("PDF name is too long.");
                }
            }

            var raw = name.ToString();
            var decoded = new StringBuilder("/");
            for (var index = 1; index < raw.Length; index++)
            {
                if (raw[index] == '#')
                {
                    if (index + 2 >= raw.Length || !byte.TryParse(raw.Substring(index + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
                    {
                        throw new InvalidDataException("Invalid escaped PDF name.");
                    }

                    decoded.Append((char)value);
                    index += 2;
                }
                else
                {
                    decoded.Append(raw[index]);
                }
            }

            return new PdfName(decoded.ToString());
        }

        Position--;
        var token = Token();
        if (token is "true" or "false" or "null")
        {
            return new PdfLiteral(token);
        }

        if (!double.TryParse(token, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
        {
            throw new InvalidDataException($"Unsupported PDF value: {token}");
        }

        var end = Position;
        if (int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var objectNumber))
        {
            try
            {
                var generationToken = Token();
                if (int.TryParse(generationToken, NumberStyles.None, CultureInfo.InvariantCulture, out var generation) && Token() == "R")
                {
                    if (objectNumber <= 0 || generation > 65535)
                    {
                        throw new InvalidDataException("Invalid PDF object reference.");
                    }

                    return new PdfReference(objectNumber, generation);
                }
            }
            catch (InvalidDataException)
            {
                // A number followed by a delimiter is not an indirect reference.
            }

            Position = end;
        }

        return new PdfNumber(number);
    }

    /// <summary>Requires the next token to equal the expected token.</summary>
    /// <param name="expected">The required token.</param>
    internal void Expect(string expected)
    {
        if (Token() != expected)
        {
            throw new InvalidDataException($"Expected PDF token {expected}.");
        }
    }

    /// <summary>Consumes the required newline after the stream keyword.</summary>
    internal void StreamNewline()
    {
        var value = Read();
        if (value == '\r')
        {
            if (Peek() == '\n')
            {
                Read();
            }
        }
        else if (value != '\n')
        {
            throw new InvalidDataException("PDF stream is missing its newline.");
        }
    }

    private static bool White(int value) => value is 0 or 9 or 10 or 12 or 13 or 32;

    private static bool Delimiter(int value) => White(value) || value is -1 or '(' or ')' or '<' or '>' or '[' or ']' or '{' or '}' or '/' or '%';

    private int Peek()
    {
        var value = input.ReadByte();
        if (value >= 0)
        {
            Position--;
        }

        return value;
    }

    private int Read()
    {
        var value = input.ReadByte();
        return value >= 0 ? value : throw new InvalidDataException("Truncated PDF syntax.");
    }

    private void ExpectByte(char expected)
    {
        if (Read() != expected)
        {
            throw new InvalidDataException("Malformed PDF delimiter.");
        }
    }

    private void Skip()
    {
        while (true)
        {
            while (White(Peek()))
            {
                Read();
            }

            if (Peek() != '%')
            {
                return;
            }

            while (input.Position < input.Length && Read() is not ('\r' or '\n'))
            {
            }
        }
    }

    private PdfString ReadString(char opening, char closing)
    {
        var result = new StringBuilder().Append(opening);
        var depth = 1;
        while (depth > 0)
        {
            var value = Read();
            result.Append((char)value);
            if (result.Length > 1048576)
            {
                throw new InvalidDataException("PDF string is too long.");
            }

            if (opening == '(' && value == '\\')
            {
                result.Append((char)Read());
            }
            else if (opening == '(' && value == '(')
            {
                if (++depth > 64)
                {
                    throw new InvalidDataException("PDF string nesting is too deep.");
                }
            }
            else if (value == closing)
            {
                depth--;
            }
            else if (opening == '<' && !White(value) && !Uri.IsHexDigit((char)value))
            {
                throw new InvalidDataException("Invalid PDF hexadecimal string.");
            }
        }

        return new(result.ToString());
    }
}
