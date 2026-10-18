using System.Globalization;
using System.Text;

namespace ExcelRenderer.Core.Pdf.Structure;

/// <summary>Represents an invariant finite number.</summary>
/// <param name="Value">The numeric value.</param>
internal sealed record PdfNumber(double Value) : PdfValue
{
    /// <inheritdoc/>
    internal override string Serialize() => double.IsFinite(Value) ? Value.ToString("0.###############", CultureInfo.InvariantCulture) : throw new InvalidDataException("A PDF number must be finite.");
}
