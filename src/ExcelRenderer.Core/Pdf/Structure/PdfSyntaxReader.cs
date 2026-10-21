using System.Globalization;
using System.Text;

namespace ExcelRenderer.Core.Pdf.Structure;

/// <summary>Parses bounded tokens without scanning stream bytes for dictionary syntax.</summary>
internal sealed class PdfSyntaxReader
{
    private readonly Stream input;
    private readonly byte[] buffer;
    private long position;
    private long inputPosition;
    private long bufferStart;
    private int bufferCount;
    private int tokens;

    /// <summary>Initializes a new instance of the <see cref="PdfSyntaxReader"/> class.</summary>
    /// <param name="input">The immutable seekable PDF stream, exclusively borrowed for this reader's lifetime; never disposed here.</param>
    /// <param name="bufferSize">The bounded syntax buffer size.</param>
    internal PdfSyntaxReader(Stream input, int bufferSize = 8192)
    {
        if (bufferSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bufferSize));
        }

        this.input = input;
        Length = input.Length;
        position = inputPosition = input.Position;
        buffer = new byte[bufferSize];
    }

    /// <summary>Gets or sets the byte position.</summary>
    internal long Position
    {
        get => position;
        set
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            position = value;
        }
    }

    /// <summary>Gets the immutable input length captured at construction.</summary>
    internal long Length { get; }

    /// <summary>Reads a bounded header or tail through the logical cursor.</summary>
    /// <param name="bytes">The destination to fill completely.</param>
    internal void ReadExactly(byte[] bytes)
    {
        var offset = 0;
        while (offset < bytes.Length)
        {
            if (!EnsureBuffer())
            {
                throw new InvalidDataException("Truncated generated PDF.");
            }

            var index = (int)(position - bufferStart);
            var count = Math.Min(bytes.Length - offset, bufferCount - index);
            Buffer.BlockCopy(buffer, index, bytes, offset, count);
            position += count;
            offset += count;
        }
    }

    /// <summary>Copies an encoded range directly, invalidating syntax read-ahead and leaving the logical cursor at its end.</summary>
    /// <param name="offset">The absolute input offset.</param>
    /// <param name="length">The exact number of bytes to copy.</param>
    /// <param name="output">The borrowed destination.</param>
    /// <param name="token">The cancellation token.</param>
    /// <param name="transferBuffer">The optional shared block transfer buffer.</param>
    internal void CopyRange(long offset, long length, Stream output, CancellationToken token, byte[]? transferBuffer = null)
    {
        transferBuffer ??= new byte[81920];
        BeginCopy(offset, length, transferBuffer);
        var remaining = length;
        while (remaining > 0)
        {
            token.ThrowIfCancellationRequested();
            var read = input.Read(transferBuffer, 0, (int)Math.Min(transferBuffer.Length, remaining));
            if (read == 0)
            {
                throw new InvalidDataException("Truncated PDF stream during finalization.");
            }

            position += read;
            inputPosition += read;
            output.Write(transferBuffer, 0, read);
            remaining -= read;
        }
    }

    /// <summary>Copies an encoded range sequentially using the same logical cursor and invalidation as synchronous copies.</summary>
    /// <param name="offset">The absolute input offset.</param>
    /// <param name="length">The exact number of bytes to copy.</param>
    /// <param name="output">The borrowed destination.</param>
    /// <param name="token">The conversion token.</param>
    /// <param name="transferBuffer">The reusable nonempty block buffer.</param>
    /// <returns>The completion of all range writes.</returns>
    internal async Task CopyRangeAsync(long offset, long length, Stream output, CancellationToken token, byte[] transferBuffer)
    {
        token.ThrowIfCancellationRequested();
        BeginCopy(offset, length, transferBuffer);
        var remaining = length;
        while (remaining > 0)
        {
            token.ThrowIfCancellationRequested();
            var read = await input.ReadAsync(transferBuffer.AsMemory(0, (int)Math.Min(transferBuffer.Length, remaining)), token).ConfigureAwait(false);
            if (read == 0)
            {
                throw new InvalidDataException("Truncated PDF stream during finalization.");
            }

            position += read;
            inputPosition += read;
            await output.WriteAsync(transferBuffer.AsMemory(0, read), token).ConfigureAwait(false);
            remaining -= read;
        }
    }

    /// <summary>Reads a whitespace-delimited token.</summary>
    /// <returns>The token.</returns>
    internal string Token()
    {
        Skip();
        var result = new StringBuilder();
        while (Position < Length && !Delimiter(Peek()))
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
            while (Position < Length && !Delimiter(Peek()))
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

    private int Peek() => EnsureBuffer() ? buffer[(int)(position - bufferStart)] : -1;

    private int Read()
    {
        var value = Peek();
        if (value < 0)
        {
            throw new InvalidDataException("Truncated PDF syntax.");
        }

        position++;
        return value;
    }

    private bool EnsureBuffer()
    {
        if (position >= Length)
        {
            return false;
        }

        var offset = position - bufferStart;
        if (offset >= 0 && offset < bufferCount)
        {
            return true;
        }

        MoveInput();
        bufferStart = position;
        bufferCount = input.Read(buffer, 0, (int)Math.Min(buffer.Length, Length - position));
        inputPosition += bufferCount;
        return bufferCount > 0;
    }

    private void BeginCopy(long offset, long length, byte[] transferBuffer)
    {
        if (transferBuffer.Length == 0)
        {
            throw new ArgumentException("A nonempty transfer buffer is required.", nameof(transferBuffer));
        }

        if (offset < 0 || length < 0 || offset > Length || length > Length - offset)
        {
            throw new InvalidDataException("Invalid generated PDF stream range.");
        }

        bufferCount = 0;
        position = offset;
        MoveInput();
    }

    private void MoveInput()
    {
        if (inputPosition != position)
        {
            input.Position = position;
            inputPosition = position;
        }
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

            while (Position < Length && Read() is not ('\r' or '\n'))
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
