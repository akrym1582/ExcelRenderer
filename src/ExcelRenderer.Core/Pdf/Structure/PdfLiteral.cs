using System.Globalization;
using System.Text;

namespace ExcelRenderer.Core.Pdf.Structure;

/// <summary>Represents a literal boolean or null.</summary>
/// <param name="Value">The validated token.</param>
internal sealed record PdfLiteral(string Value) : PdfValue
{
    /// <inheritdoc/>
    internal override string Serialize() => Value;
}
