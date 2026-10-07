using ExcelRenderer.Model;

namespace ExcelRenderer.Excel;

/// <summary>Interns immutable style values within one workbook.</summary>
internal sealed class StylePool
{
    private readonly Dictionary<FontStyle, FontStyle> _fonts = new();
    private readonly Dictionary<BorderSide, BorderSide> _sides = new();
    private readonly Dictionary<BorderStyle, BorderStyle> _borders = new();
    private readonly Dictionary<CellStyle, CellStyle> _styles = new();

    /// <summary>Shares equal converted styles and their components.</summary>
    /// <param name="style">The converted, data-type-aware style.</param>
    /// <returns>The canonical style.</returns>
    internal CellStyle Intern(CellStyle style)
    {
        if (_styles.TryGetValue(style, out var cached))
        {
            return cached;
        }

        var font = Share(_fonts, style.Font);
        var border = style.Border is not { } value ? null : Intern(value);
        var result = style with { Font = font, Border = border };
        _styles.Add(result, result);
        return result;
    }

    /// <summary>Shares an immutable border and its sides, including merged-cell fragments.</summary>
    /// <param name="border">The converted border.</param>
    /// <returns>The canonical border.</returns>
    internal BorderStyle Intern(BorderStyle border) => Share(_borders, new BorderStyle(
        Side(border.Left),
        Side(border.Top),
        Side(border.Right),
        Side(border.Bottom)));

    private static T Share<T>(Dictionary<T, T> pool, T value)
        where T : notnull
    {
        if (!pool.TryGetValue(value, out var result))
        {
            result = value;
            pool.Add(value, result);
        }

        return result;
    }

    private BorderSide? Side(BorderSide? side) => side is null ? null : Share(_sides, side);
}
