using System.Globalization;
using System.Text;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;

namespace ExcelRenderer.PostScript;

/// <summary>Serializes ASCII PostScript without locale-dependent numbers or executable input strings.</summary>
internal sealed class PostScriptWriter : IDisposable
{
    private readonly StreamWriter writer;
    private readonly CancellationToken token;
    private int metadataId;

    /// <summary>Initializes a new instance of the <see cref="PostScriptWriter"/> class.</summary>
    /// <param name="stream">The borrowed destination.</param>
    /// <param name="token">The cancellation token.</param>
    internal PostScriptWriter(Stream stream, CancellationToken token)
    {
        writer = new(stream, Encoding.ASCII, 16384, leaveOpen: true) { NewLine = "\n" };
        this.token = token;
    }

    /// <inheritdoc/>
    public void Dispose() => writer.Dispose();

    /// <summary>Formats a finite number in point units.</summary>
    /// <param name="value">The number.</param>
    /// <returns>An ASCII PostScript real.</returns>
    internal static string Number(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "PostScript numbers must be finite.");
        }

        // PostScript does not accept the exponent syntax emitted by round-trip .NET formatting.
        if (Math.Abs(value) > 1e15)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "PostScript coordinates exceed the supported numeric range.");
        }

        return value.ToString("0.################", CultureInfo.InvariantCulture);
    }

    /// <summary>Rejects partial alpha and permits invisible elements to be omitted.</summary>
    /// <param name="color">The source color.</param>
    /// <returns>Whether the element is visible.</returns>
    internal static bool Visible(ReportColor color)
    {
        if (color.Alpha is > 0 and < 255)
        {
            throw new PostScriptFailure("PostScriptTransparencyUnsupported", "PostScript output does not support partial alpha in fills, strokes or text.");
        }

        return color.Alpha != 0;
    }

    /// <summary>Flushes the ASCII serializer without closing the spool.</summary>
    internal void Flush() => writer.Flush();

    /// <summary>Writes a trusted instruction line.</summary>
    /// <param name="line">Only implementation-generated syntax.</param>
    internal void Line(string line)
    {
        token.ThrowIfCancellationRequested();
        writer.WriteLine(line);
    }

    /// <summary>Writes bounded ASCII hex strings or image samples.</summary>
    /// <param name="data">The source bytes.</param>
    /// <param name="start">The start offset.</param>
    /// <param name="length">The byte count.</param>
    internal void Hex(byte[] data, int start, int length)
    {
        for (var offset = 0; offset < length; offset += 40)
        {
            var count = Math.Min(40, length - offset);
            Line(BitConverter.ToString(data, start + offset, count).Replace("-", string.Empty, StringComparison.Ordinal));
        }
    }

    /// <summary>Writes a rectangle path in the current point coordinate system.</summary>
    /// <param name="bounds">The rectangle.</param>
    internal void Rectangle(ReportRect bounds) => Line($"newpath {Number(bounds.X)} {Number(bounds.Y)} {Number(bounds.Width)} {Number(bounds.Height)} rectpath");

    /// <summary>Intersects the current clip with a rectangle.</summary>
    /// <param name="bounds">The rectangle.</param>
    internal void Clip(ReportRect bounds)
    {
        Rectangle(bounds);
        Line("clip newpath");
    }

    /// <summary>Sets an opaque RGB color.</summary>
    /// <param name="color">The validated color.</param>
    internal void Color(ReportColor color) => Line($"{Number(color.Red / 255d)} {Number(color.Green / 255d)} {Number(color.Blue / 255d)} setrgbcolor");

    /// <summary>Rotates around a point in the downward Y coordinate system.</summary>
    /// <param name="angle">The angle.</param>
    /// <param name="x">The pivot X.</param>
    /// <param name="y">The pivot Y.</param>
    internal void Rotate(double angle, double x, double y)
    {
        Line($"{Number(x)} {Number(y)} translate {Number(angle)} rotate {Number(-x)} {Number(-y)} translate");
    }

    /// <summary>Writes versioned, chunked, non-DSC original-text metadata.</summary>
    /// <param name="write">Writes one binary record as documented in docs/postscript.md.</param>
    internal void Metadata(Action<BinaryWriter> write)
    {
        using var data = new MemoryStream();
        using (var binary = new BinaryWriter(data, new UTF8Encoding(false, true), leaveOpen: true))
        {
            write(binary);
        }

        var base64 = Convert.ToBase64String(data.ToArray());
        var count = (base64.Length + 159) / 160;
        var id = ++metadataId;
        for (var part = 0; part < count; part++)
        {
            Line($"%ExcelRenderer-Text-v1: {id} {part + 1}/{count} {base64.Substring(part * 160, Math.Min(160, base64.Length - (part * 160)))}");
        }
    }
}
