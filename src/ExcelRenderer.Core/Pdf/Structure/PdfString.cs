using System.Globalization;
using System.Text;

namespace ExcelRenderer.Core.Pdf.Structure;

/// <summary>Preserves a parsed string token verbatim.</summary>
/// <param name="Token">The escaped literal or hexadecimal token.</param>
internal sealed record PdfString(string Token) : PdfValue
{
    /// <summary>Creates an unambiguous UTF-16BE hexadecimal PDF string.</summary>
    /// <param name="text">The original Unicode text.</param>
    /// <returns>The encoded string.</returns>
    internal static PdfString Unicode(string text) => new("<FEFF" + BitConverter.ToString(Encoding.BigEndianUnicode.GetBytes(text)).Replace("-", string.Empty, StringComparison.Ordinal) + ">");

    /// <inheritdoc/>
    internal override string Serialize() => Token;
}
