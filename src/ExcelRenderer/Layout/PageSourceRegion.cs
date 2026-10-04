using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>Maps a visible sheet rectangle into one body or repeated-title placement.</summary>
/// <param name="SourceBounds">The sheet-space clip.</param>
/// <param name="PageBounds">The page-space clip.</param>
/// <param name="Scale">The sheet-to-page scale.</param>
/// <param name="IsTitle">Whether this is a repeated-title placement.</param>
internal sealed record PageSourceRegion(ReportRect SourceBounds, ReportRect PageBounds, double Scale, bool IsTitle)
{
    /// <summary>Gets the original inclusive cell region.</summary>
    internal CellRange? Cells { get; init; }

    /// <summary>Maps a sheet-space rectangle without clipping or changing its dimensions.</summary>
    /// <param name="bounds">The source rectangle.</param>
    /// <returns>The page rectangle.</returns>
    internal ReportRect Map(ReportRect bounds) => new(
        PageBounds.X + ((bounds.X - SourceBounds.X) * Scale),
        PageBounds.Y + ((bounds.Y - SourceBounds.Y) * Scale),
        bounds.Width * Scale,
        bounds.Height * Scale);
}
