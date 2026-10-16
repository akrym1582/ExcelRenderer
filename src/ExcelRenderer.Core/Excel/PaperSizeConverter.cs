namespace ExcelRenderer.Core.Excel;

/// <summary>Maps OOXML paper identifiers to the established product dimensions in PDF points.</summary>
internal static class PaperSizeConverter
{
    /// <summary>Determines whether an identifier has an explicit product dimension.</summary>
    /// <param name="paperSize">The OOXML identifier.</param>
    /// <returns>Whether the identifier is supported.</returns>
    internal static bool IsSupported(int paperSize) => paperSize is 1 or 2 or 5 or 8 or 9 or 11 or 12 or 13;

    /// <summary>Reads the supported paper size, defaulting to A4 for unknown identifiers.</summary>
    /// <param name="paperSize">The OOXML paperSize identifier.</param>
    /// <returns>Portrait width and height in points.</returns>
    internal static (double Width, double Height) ToPoints(int paperSize) => paperSize switch
    {
        1 or 2 => (612, 792),
        5 => (612, 1008),
        8 => (841.89, 1190.55),
        11 => (419.53, 595.28),
        12 => (708.66, 1000.63),
        13 => (498.9, 708.66),
        _ => (595.276, 841.89),
    };
}
