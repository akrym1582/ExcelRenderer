using System.Globalization;

namespace ExcelRenderer.Model;

/// <summary>Formats cell ranges for diagnostics and conversion manifests.</summary>
internal static class CellRangeFormatter
{
    /// <summary>Formats a cell or range using A1 notation.</summary>
    /// <param name="range">The cell range.</param>
    /// <returns>The formatted address or range.</returns>
    internal static string Range(CellRange range) => range.First == range.Last
        ? Address(range.First) : Address(range.First) + ":" + Address(range.Last);

    private static string Address(CellAddress address)
    {
        var n = address.Column;
        var letters = string.Empty;
        while (n > 0)
        {
            n--;
            letters = (char)('A' + (n % 26)) + letters;
            n /= 26;
        }

        return letters + address.Row.ToString(CultureInfo.InvariantCulture);
    }
}
