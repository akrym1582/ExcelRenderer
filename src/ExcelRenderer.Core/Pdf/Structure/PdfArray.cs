using System.Globalization;
using System.Text;

namespace ExcelRenderer.Core.Pdf.Structure;

/// <summary>Represents a bounded array.</summary>
/// <param name="Items">The ordered values.</param>
internal sealed record PdfArray(IReadOnlyList<PdfValue> Items) : PdfValue
{
    /// <inheritdoc/>
    internal override string Serialize() => "[" + string.Join(" ", Items.Select(item => item.Serialize())) + "]";
}
