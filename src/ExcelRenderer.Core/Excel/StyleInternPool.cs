namespace ExcelRenderer.Core.Excel;

/// <summary>Canonicalizes immutable styles and their components for either model boundary.</summary>
/// <typeparam name="TFont">The font value.</typeparam>
/// <typeparam name="TSide">The border-side value.</typeparam>
/// <typeparam name="TBorder">The border value.</typeparam>
/// <typeparam name="TStyle">The cell-style value.</typeparam>
internal sealed class StyleInternPool<TFont, TSide, TBorder, TStyle>
    where TFont : notnull
    where TSide : class
    where TBorder : class
    where TStyle : notnull
{
    private readonly Dictionary<TFont, TFont> fonts = new();
    private readonly Dictionary<TSide, TSide> sides = new();
    private readonly Dictionary<TBorder, TBorder> borders = new();
    private readonly Dictionary<TStyle, TStyle> styles = new();
    private readonly Func<TStyle, TFont> font;
    private readonly Func<TStyle, TBorder?> border;
    private readonly Func<TStyle, TFont, TBorder?, TStyle> compose;
    private readonly Func<TBorder, Func<TSide?, TSide?>, TBorder> composeBorder;

    /// <summary>Initializes a new instance of the <see cref="StyleInternPool{TFont, TSide, TBorder, TStyle}"/> class.</summary>
    /// <param name="font">The font accessor.</param>
    /// <param name="border">The border accessor.</param>
    /// <param name="compose">The immutable style constructor.</param>
    /// <param name="composeBorder">The immutable border constructor.</param>
    internal StyleInternPool(Func<TStyle, TFont> font, Func<TStyle, TBorder?> border, Func<TStyle, TFont, TBorder?, TStyle> compose, Func<TBorder, Func<TSide?, TSide?>, TBorder> composeBorder)
    {
        this.font = font;
        this.border = border;
        this.compose = compose;
        this.composeBorder = composeBorder;
    }

    /// <summary>Shares a cell style and its components.</summary>
    /// <param name="value">The original style.</param>
    /// <returns>The canonical value.</returns>
    internal TStyle InternStyle(TStyle value)
    {
        if (styles.TryGetValue(value, out var cached))
        {
            return cached;
        }

        var result = compose(value, Share(fonts, font(value)), border(value) is { } source ? InternBorder(source) : null);
        styles.Add(result, result);
        return result;
    }

    /// <summary>Shares a border and all its sides.</summary>
    /// <param name="value">The original border.</param>
    /// <returns>The canonical border.</returns>
    internal TBorder InternBorder(TBorder value) => Share(borders, composeBorder(value, side => side is null ? null : Share(sides, side)));

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
}
