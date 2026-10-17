using System.Collections.Generic;

namespace ExcelRenderer.OpenXml.Formatting;

/// <summary>Provides NumberFormats metadata.</summary>
internal static class NumberFormats
{
    private static readonly Dictionary<int, string> Codes = new Dictionary<int, string>
    {
        [0] = "General",
        [1] = "0",
        [2] = "0.00",
        [3] = "#,##0",
        [4] = "#,##0.00",
        [5] = "\"$\"#,##0_);(\"$\"#,##0)",
        [6] = "\"$\"#,##0_);[Red](\"$\"#,##0)",
        [7] = "\"$\"#,##0.00_);(\"$\"#,##0.00)",
        [8] = "\"$\"#,##0.00_);[Red](\"$\"#,##0.00)",
        [9] = "0%",
        [10] = "0.00%",
        [11] = "0.00E+00",
        [12] = "# ?/?",
        [13] = "# ??/??",
        [14] = "mm-dd-yy",
        [15] = "d-mmm-yy",
        [16] = "d-mmm",
        [17] = "mmm-yy",
        [18] = "h:mm AM/PM",
        [19] = "h:mm:ss AM/PM",
        [20] = "h:mm",
        [21] = "h:mm:ss",
        [22] = "m/d/yy h:mm",
        [37] = "#,##0 ;(#,##0)",
        [38] = "#,##0 ;[Red](#,##0)",
        [39] = "#,##0.00;(#,##0.00)",
        [40] = "#,##0.00;[Red](#,##0.00)",
        [41] = "_(* #,##0_);_(* (#,##0);_(* \"-\"_);_(@_)",
        [42] = "_(\"$\"* #,##0_);_(\"$\"* (#,##0);_(\"$\"* \"-\"_);_(@_)",
        [43] = "_(* #,##0.00_);_(* (#,##0.00);_(* \"-\"??_);_(@_)",
        [44] = "_(\"$\"* #,##0.00_);_(\"$\"* (#,##0.00);_(\"$\"* \"-\"??_);_(@_)",
        [45] = "mm:ss",
        [46] = "[h]:mm:ss",
        [47] = "mmss.0",
        [48] = "##0.0E+0",
        [49] = "@",
    };

    /// <summary>Provides Builtin metadata.</summary>
    /// <returns>The resulting value.</returns>
    /// <param name="id">The id value.</param>
    internal static string? Builtin(int id) => Codes.TryGetValue(id, out var code) ? code : null;
}
