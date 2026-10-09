using ExcelRenderer.Core.Model;

namespace ExcelRenderer.Core.Excel;

/// <summary>Interns immutable style values within one workbook.</summary>
internal sealed class StylePool
{
    private readonly ExcelRenderer.Core.Excel.StyleInternPool<FontStyle, BorderSide, BorderStyle, CellStyle> pool = new(
        style => style.Font,
        style => style.Border,
        (style, font, border) => style with { Font = font, Border = border },
        (border, side) => new(side(border.Left), side(border.Top), side(border.Right), side(border.Bottom)));

    /// <summary>Shares equal converted styles and their components.</summary>
    /// <param name="style">The converted, data-type-aware style.</param>
    /// <returns>The canonical style.</returns>
    internal CellStyle Intern(CellStyle style) => pool.InternStyle(style);

    /// <summary>Shares an immutable border and its sides, including merged-cell fragments.</summary>
    /// <param name="border">The converted border.</param>
    /// <returns>The canonical border.</returns>
    internal BorderStyle Intern(BorderStyle border) => pool.InternBorder(border);
}
