using System.Globalization;
using System.Text;

namespace ExcelRenderer.Core.Pdf.Structure;

/// <summary>Preserves every entry in a generated dictionary.</summary>
/// <param name="Entries">The entries including leading slashes.</param>
internal sealed record PdfMap(Dictionary<string, PdfValue> Entries) : PdfValue
{
    /// <summary>Gets a value or null when the key is absent.</summary>
    /// <param name="key">The PDF name.</param>
    /// <returns>The parsed value.</returns>
    internal PdfValue? Get(string key) => Entries.TryGetValue(key, out var value) ? value : null;

    /// <inheritdoc/>
    internal override string Serialize() => "<<" + string.Join("\n", Entries.Select(entry => new PdfName(entry.Key).Serialize() + " " + entry.Value.Serialize())) + ">>";
}
