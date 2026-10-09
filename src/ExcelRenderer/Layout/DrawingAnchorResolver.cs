using ExcelRenderer.Model;

namespace ExcelRenderer.Layout;

/// <summary>Resolves DrawingML anchors against the shared sheet geometry.</summary>
internal static class DrawingAnchorResolver
{
    /// <summary>Resolves an object's original point rectangle without a pixel round trip.</summary>
    /// <param name="context">The shared row and column geometry.</param>
    /// <param name="fallbackAnchor">The legacy anchor cell.</param>
    /// <param name="fallbackOffsetX">The legacy horizontal offset.</param>
    /// <param name="fallbackOffsetY">The legacy vertical offset.</param>
    /// <param name="fallbackWidth">The legacy object width.</param>
    /// <param name="fallbackHeight">The legacy object height.</param>
    /// <param name="anchor">The original DrawingML anchor, if available.</param>
    /// <param name="bounds">The resolved sheet-space rectangle.</param>
    /// <returns><see langword="true"/> when all required geometry exists.</returns>
    internal static bool TryResolve(
        ReportLayoutContext context,
        CellAddress fallbackAnchor,
        double fallbackOffsetX,
        double fallbackOffsetY,
        double fallbackWidth,
        double fallbackHeight,
        DrawingAnchor? anchor,
        out ReportRect bounds)
    {
        var result = Core.Layout.DrawingAnchorResolver.TryResolve(CoreIntegration.CoreLayoutContextAdapter.CreateGeometry(context), CoreIntegration.CoreModelAdapter.ToCore(fallbackAnchor), fallbackOffsetX, fallbackOffsetY, fallbackWidth, fallbackHeight, anchor is null ? null : CoreIntegration.CoreModelAdapter.ToCore(anchor), out var coreBounds);
        bounds = CoreIntegration.CoreModelAdapter.ToPublic(coreBounds);
        return result;
    }
}
