using System.Text;

namespace ExcelRenderer.Core.Excel;

/// <summary>Provides deterministic column metrics when the original Normal font is unavailable.</summary>
internal static class SourceFontFallbackMetrics
{
    /// <summary>Resolves a known substitution profile or the generic compatibility width.</summary>
    /// <param name="normal">The original workbook Normal font, if available.</param>
    /// <returns>The maximum digit width in pixels at 96 DPI.</returns>
    internal static double MaximumDigitWidth(NormalFontMetadata? normal)
    {
        var family = normal?.Family.Normalize(NormalizationForm.FormKC).Replace(" ", string.Empty);
        if (normal is not null && double.IsFinite(normal.Size) && normal.Size > 0 &&
            (string.Equals(family, "MSPGothic", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(family, "MSPゴシック", StringComparison.OrdinalIgnoreCase)))
        {
            // Calc substitutes Noto Sans for Latin digits in MS P Gothic. Its
            // tabular advance is 572/1000 em, rounded to twips during import.
            // One pixel at 96 DPI is 15 twips. Keep this profile independent
            // of the drawing font and of installed system fonts.
            return Math.Max(1, Math.Round(normal.Size * 0.572 * 20, MidpointRounding.AwayFromZero) / 15);
        }

        return 7;
    }
}
