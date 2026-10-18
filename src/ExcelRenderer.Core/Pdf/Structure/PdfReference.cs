using System.Globalization;
using System.Text;

namespace ExcelRenderer.Core.Pdf.Structure;

/// <summary>Represents an indirect reference.</summary>
/// <param name="Number">The object number.</param>
/// <param name="Generation">The generation number.</param>
internal sealed record PdfReference(int Number, int Generation = 0) : PdfValue
{
    /// <inheritdoc/>
    internal override string Serialize() => string.Format(CultureInfo.InvariantCulture, "{0} {1} R", Number, Generation);
}
