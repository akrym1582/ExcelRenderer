using System.Globalization;
using System.Text;

namespace ExcelRenderer.Core.Pdf.Structure;

/// <summary>Represents bounded PDF syntax used only for our generated document finalization.</summary>
internal abstract record PdfValue
{
    /// <summary>Serializes PDF syntax using invariant ASCII.</summary>
    /// <returns>The PDF token.</returns>
    internal abstract string Serialize();
}
