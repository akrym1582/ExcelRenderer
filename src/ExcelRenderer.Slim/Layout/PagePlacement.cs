using ExcelRenderer.Slim.Model;

namespace ExcelRenderer.Slim.Layout;

/// <summary>Contains the immutable coordinate transform and body clip for one output page.</summary>
internal sealed class PagePlacement
{
    private readonly double _centerX;
    private readonly double _centerY;
    private readonly PageSettings _settings;

    /// <summary>Initializes a new instance of the <see cref="PagePlacement"/> class.Provides the backend-specific pagination or text operation.</summary>
    /// <param name="settings">The settings value.</param>
    /// <param name="horizontal">The horizontal value.</param>
    /// <param name="vertical">The vertical value.</param>
    /// <param name="horizontalEnd">The horizontalEnd value.</param>
    /// <param name="verticalEnd">The verticalEnd value.</param>
    /// <param name="repeatedWidth">The repeatedWidth value.</param>
    /// <param name="repeatedHeight">The repeatedHeight value.</param>
    /// <param name="scale">The scale value.</param>
    internal PagePlacement(
        PageSettings settings,
        PageBand horizontal,
        PageBand vertical,
        double horizontalEnd,
        double verticalEnd,
        double repeatedWidth,
        double repeatedHeight,
        double scale)
    {
        _settings = settings;
        Horizontal = horizontal;
        Vertical = vertical;
        RepeatedWidth = repeatedWidth;
        RepeatedHeight = repeatedHeight;
        Scale = scale;
        var bodyWidth = settings.Width - settings.MarginLeft - settings.MarginRight;
        var bodyHeight = settings.Height - settings.MarginTop - settings.MarginBottom;
        var occupiedWidth = Math.Min(bodyWidth, ((horizontalEnd - horizontal.Start) + repeatedWidth) * scale);
        var occupiedHeight = Math.Min(bodyHeight, ((verticalEnd - vertical.Start) + repeatedHeight) * scale);
        _centerX = settings.HorizontalCentered ? Math.Max(0, (bodyWidth - occupiedWidth) / 2) : 0;
        _centerY = settings.VerticalCentered ? Math.Max(0, (bodyHeight - occupiedHeight) / 2) : 0;
        BodyClip = new(
            settings.MarginLeft + _centerX + (repeatedWidth * scale),
            settings.MarginTop + _centerY + (repeatedHeight * scale),
            Math.Max(0, occupiedWidth - (repeatedWidth * scale)),
            Math.Max(0, occupiedHeight - (repeatedHeight * scale)));
    }

    /// <summary>Gets provides the backend-specific pagination or text operation.</summary>
    internal ReportRect BodyClip { get; }

    /// <summary>Gets provides the backend-specific pagination or text operation.</summary>
    internal PageBand Horizontal { get; }

    /// <summary>Gets provides the backend-specific pagination or text operation.</summary>
    internal PageBand Vertical { get; }

    /// <summary>Gets provides the backend-specific pagination or text operation.</summary>
    internal double RepeatedHeight { get; }

    /// <summary>Gets provides the backend-specific pagination or text operation.</summary>
    internal double RepeatedWidth { get; }

    /// <summary>Gets provides the backend-specific pagination or text operation.</summary>
    internal double Scale { get; }

    /// <summary>Provides the backend-specific pagination or text operation.</summary>
    /// <returns>The calculated value.</returns>
    /// <param name="bounds">The bounds value.</param>
    internal ReportRect MapBodyObjectBounds(ReportRect bounds) => new(
        ((bounds.X - Horizontal.Start + RepeatedWidth) * Scale) + _settings.MarginLeft + _centerX,
        ((bounds.Y - Vertical.Start + RepeatedHeight) * Scale) + _settings.MarginTop + _centerY,
        bounds.Width * Scale,
        bounds.Height * Scale);

    /// <summary>Provides the backend-specific pagination or text operation.</summary>
    /// <returns>The calculated value.</returns>
    /// <param name="x">The x value.</param>
    /// <param name="repeatsTitles">The repeatsTitles value.</param>
    /// <param name="isTitle">The isTitle value.</param>
    /// <param name="titleStart">The titleStart value.</param>
    internal double MapCellX(double x, bool repeatsTitles, bool isTitle, double titleStart) =>
        (GetPosition(x, Horizontal.Start, repeatsTitles, isTitle, titleStart, RepeatedWidth) * Scale) +
        _settings.MarginLeft + _centerX;

    /// <summary>Provides the backend-specific pagination or text operation.</summary>
    /// <returns>The calculated value.</returns>
    /// <param name="y">The y value.</param>
    /// <param name="repeatsTitles">The repeatsTitles value.</param>
    /// <param name="isTitle">The isTitle value.</param>
    /// <param name="titleStart">The titleStart value.</param>
    internal double MapCellY(double y, bool repeatsTitles, bool isTitle, double titleStart) =>
        (GetPosition(y, Vertical.Start, repeatsTitles, isTitle, titleStart, RepeatedHeight) * Scale) +
        _settings.MarginTop + _centerY;

    private static double GetPosition(
        double position,
        double bandStart,
        bool repeatsTitles,
        bool isTitle,
        double titleStart,
        double repeatedSize)
    {
        if (!repeatsTitles)
        {
            return position - bandStart;
        }

        return isTitle ? position - titleStart : position - bandStart + repeatedSize;
    }
}
