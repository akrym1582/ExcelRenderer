using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>
/// Builds one immutable render page from a pair of half-open sheet-coordinate bands.
/// Coordinates and sizes are PDF points; an infinite band end includes all remaining content.
/// </summary>
internal sealed class RenderPageBuilder
{
    private readonly Core.Layout.RenderPageBuilder inner;

    /// <summary>Initializes a new instance of the <see cref="RenderPageBuilder"/> class without mutating the supplied context.</summary>
    /// <param name="context">The completed sheet layout to read.</param>
    /// <param name="bodyColumns">Visible body-column indices.</param>
    /// <param name="bodyRows">Visible body-row indices.</param>
    /// <param name="titleColumns">Visible repeated title-column indices.</param>
    /// <param name="titleRows">Visible repeated title-row indices.</param>
    /// <param name="titleColumnEnd">The exclusive title-column end in sheet points.</param>
    /// <param name="titleRowEnd">The exclusive title-row end in sheet points.</param>
    /// <param name="titleWidth">The repeated-title width in points.</param>
    /// <param name="titleHeight">The repeated-title height in points.</param>
    /// <param name="scale">The sheet-to-page scale.</param>
    internal RenderPageBuilder(
        ReportLayoutContext context,
        IReadOnlyList<int> bodyColumns,
        IReadOnlyList<int> bodyRows,
        IReadOnlyList<int> titleColumns,
        IReadOnlyList<int> titleRows,
        double titleColumnEnd,
        double titleRowEnd,
        double titleWidth,
        double titleHeight,
        double scale)
    {
        inner = new(CoreIntegration.CoreLayoutContextAdapter.CreatePage(context), bodyColumns, bodyRows, titleColumns, titleRows, titleColumnEnd, titleRowEnd, titleWidth, titleHeight, scale);
    }

    /// <summary>
    /// Selects and maps cells, images, and shapes for one page. Repeated title cells use the
    /// title origin, while body objects use the body origin and are clipped outside that body.
    /// </summary>
    /// <param name="pageNumber">The one-based page number.</param>
    /// <param name="horizontal">The half-open horizontal source band.</param>
    /// <param name="vertical">The half-open vertical source band.</param>
    /// <returns>An immutable page containing the selected and mapped objects.</returns>
    internal RenderPage Build(int pageNumber, PageBand horizontal, PageBand vertical)
        => CoreIntegration.CorePageAdapter.ToPublic(inner.Build(pageNumber, CoreIntegration.CoreModelAdapter.ToCore(horizontal), CoreIntegration.CoreModelAdapter.ToCore(vertical)));
}
