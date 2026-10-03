using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>Expands and positions the header and footer for a finalized page number.</summary>
internal static class HeaderFooterLayout
{
    /// <summary>Provides the backend-specific pagination or text operation.</summary>
    /// <returns>The calculated value.</returns>
    /// <param name="sheet">The sheet value.</param>
    /// <param name="pageNumber">The pageNumber value.</param>
    /// <param name="pageCount">The pageCount value.</param>
    internal static IReadOnlyList<RenderText> Create(ReportSheet sheet, int pageNumber, int pageCount)
    {
        if (sheet.HeaderFooter is not { } headerFooter)
        {
            return [];
        }

        var settings = sheet.PageSettings;
        var header = pageNumber == 1 && headerFooter.FirstPageHeader is not null
            ? headerFooter.FirstPageHeader
            : pageNumber % 2 == 0 && headerFooter.EvenPageHeader is not null
                ? headerFooter.EvenPageHeader
                : headerFooter.Header;
        var footer = pageNumber == 1 && headerFooter.FirstPageFooter is not null
            ? headerFooter.FirstPageFooter
            : pageNumber % 2 == 0 && headerFooter.EvenPageFooter is not null
                ? headerFooter.EvenPageFooter
                : headerFooter.Footer;
        var width = settings.Width - settings.MarginLeft - settings.MarginRight;
        return CreateSection(header, 0, settings.MarginTop)
            .Concat(CreateSection(footer, settings.Height - settings.MarginBottom, settings.MarginBottom))
            .ToArray();

        IEnumerable<RenderText> CreateSection(HeaderFooterSection section, double y, double height)
        {
            var style = CellStyle.Default with { VerticalAlignment = VerticalAlignment.Center };
            return new[]
            {
                new RenderText(new(settings.MarginLeft, y, width, height), ResolveFields(section.Left), style),
                new RenderText(
                    new(
                        settings.MarginLeft,
                        y,
                        width,
                        height),
                    ResolveFields(section.Center),
                    style with { HorizontalAlignment = HorizontalAlignment.Center }),
                new RenderText(
                    new(
                        settings.MarginLeft,
                        y,
                        width,
                        height),
                    ResolveFields(section.Right),
                    style with { HorizontalAlignment = HorizontalAlignment.Right }),
            }.Where(text => !string.IsNullOrEmpty(text.Text));
        }

        string ResolveFields(string text) => text
            .Replace("&P", pageNumber.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("&N", pageCount.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("&A", sheet.Name, StringComparison.OrdinalIgnoreCase)
            .Replace("&D", DateTime.Today.ToShortDateString(), StringComparison.OrdinalIgnoreCase)
            .Replace("&T", DateTime.Now.ToShortTimeString(), StringComparison.OrdinalIgnoreCase);
    }
}
