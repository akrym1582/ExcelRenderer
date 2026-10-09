using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>Contains the immutable coordinate transform and body clip for one output page.</summary>
internal sealed class PagePlacement
{
    private readonly Core.Layout.PagePlacement inner;

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
        inner = new(
            CoreIntegration.CoreModelAdapter.ToCore(settings),
            new(horizontal.Start, horizontal.End),
            new(vertical.Start, vertical.End),
            horizontalEnd,
            verticalEnd,
            repeatedWidth,
            repeatedHeight,
            scale);
        Horizontal = horizontal;
        Vertical = vertical;
        RepeatedWidth = repeatedWidth;
        RepeatedHeight = repeatedHeight;
        Scale = scale;
        var clip = inner.BodyClip;
        BodyClip = new(clip.X, clip.Y, clip.Width, clip.Height);
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
    internal ReportRect MapBodyObjectBounds(ReportRect bounds)
    {
        var mapped = inner.MapBodyObjectBounds(new(bounds.X, bounds.Y, bounds.Width, bounds.Height));
        return new(mapped.X, mapped.Y, mapped.Width, mapped.Height);
    }

    /// <summary>Provides the backend-specific pagination or text operation.</summary>
    /// <returns>The calculated value.</returns>
    /// <param name="x">The x value.</param>
    /// <param name="repeatsTitles">The repeatsTitles value.</param>
    /// <param name="isTitle">The isTitle value.</param>
    /// <param name="titleStart">The titleStart value.</param>
    internal double MapCellX(double x, bool repeatsTitles, bool isTitle, double titleStart) =>
        inner.MapCellX(x, repeatsTitles, isTitle, titleStart);

    /// <summary>Provides the backend-specific pagination or text operation.</summary>
    /// <returns>The calculated value.</returns>
    /// <param name="y">The y value.</param>
    /// <param name="repeatsTitles">The repeatsTitles value.</param>
    /// <param name="isTitle">The isTitle value.</param>
    /// <param name="titleStart">The titleStart value.</param>
    internal double MapCellY(double y, bool repeatsTitles, bool isTitle, double titleStart) =>
        inner.MapCellY(y, repeatsTitles, isTitle, titleStart);
}
