using System.Globalization;
using System.Text;

namespace ExcelRenderer.Core.Pdf.Structure;

/// <summary>Represents a name token including its leading slash.</summary>
/// <param name="Value">The escaped PDF name.</param>
internal sealed record PdfName(string Value) : PdfValue
{
    /// <inheritdoc/>
    internal override string Serialize()
    {
        var result = new StringBuilder("/");
        foreach (var character in Value.Substring(1))
        {
            if (character <= 32 || character >= 127 || "()<>[]{}/%#".Contains(character))
            {
                result.Append('#').Append(((int)character).ToString("X2", CultureInfo.InvariantCulture));
            }
            else
            {
                result.Append(character);
            }
        }

        return result.ToString();
    }
}
