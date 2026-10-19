using ExcelRenderer.Core.Fonts.TrueType;

namespace ExcelRenderer.Core.Fonts;

/// <summary>Applies PDF format restrictions only to faces actually used in PDF output.</summary>
internal static class FontCapabilities
{
    private static readonly string[] VariableTables = ["fvar", "gvar", "avar", "HVAR", "VVAR", "MVAR", "cvar"];

    /// <summary>Validates PDF format eligibility before selecting the color drawing route.</summary>
    /// <param name="face">The selected standalone face.</param>
    /// <param name="token">The cancellation token.</param>
    /// <returns>Whether the existing native color route is required.</returns>
    internal static bool ValidatePdf(SfntReader face, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (face.Version != 0x00010000 || face.Tables.ContainsKey("CFF ") || face.Tables.ContainsKey("CFF2"))
        {
            throw new NotSupportedException($"PDF requires static TrueType: unsupported {Format(face.Version)} / CFF/CFF2 outline format.");
        }

        var variable = VariableTables.FirstOrDefault(face.Tables.ContainsKey);
        if (variable is not null)
        {
            throw new NotSupportedException($"PDF does not support variable fonts (table '{variable}'), including default instances and variable color faces.");
        }

        // This is the same set of native color formats as the existing Skia route.
        var color = face.Tables.ContainsKey("COLR") || face.Tables.ContainsKey("CBDT") || face.Tables.ContainsKey("sbix");
        TrueTypeFontValidator.ValidateMetrics(face, token);
        var os2 = face.Get("OS/2", 10);
        var restrictions = SfntReader.U16(os2, 8);
        if ((restrictions & 0x0002) != 0 || (!color && (restrictions & 0x0300) != 0))
        {
            throw new NotSupportedException($"PDF font embedding/subsetting is prohibited by OS/2.fsType=0x{restrictions:X4}.");
        }

        return color;
    }

    private static string Format(uint version) => version switch
    {
        0x4f54544f => "OTTO",
        0x774f4646 => "WOFF",
        0x774f4632 => "WOFF2",
        0x74727565 => "legacy true",
        0x74797031 => "Type1 typ1",
        0x74746366 => "unextracted TTC",
        _ => $"sfnt 0x{version:X8}",
    };
}
