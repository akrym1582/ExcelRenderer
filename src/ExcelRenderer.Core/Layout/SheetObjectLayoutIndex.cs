namespace ExcelRenderer.Core.Layout;

/// <summary>Shares neutral positioned object geometry across page-local builders.</summary>
internal sealed class SheetObjectLayoutIndex
{
    /// <summary>Initializes a new instance of the <see cref="SheetObjectLayoutIndex"/> class.</summary>
    /// <param name="context">The completed product geometry.</param>
    internal SheetObjectLayoutIndex(ReportLayoutContext context)
    {
        Objects = context.Policy.GetObjectGeometry(context).Select(item =>
        {
            return DrawingAnchorResolver.TryResolve(context, item.Bounds, out var bounds)
                ? item with { Bounds = bounds, Visual = context.Policy.ResolveObjectVisualBounds(item, bounds, context.Sheet) } : null;
        }).Where(item => item is not null).Select(item => item!).ToArray();
        Bands = new(Objects.Select((item, index) => (index, item.Visual.Y, item.Visual.Y + item.Visual.Height)));
    }

    /// <summary>Gets immutable positioned object references in original order.</summary>
    internal CoreObjectGeometry[] Objects { get; }

    /// <summary>Gets the shared visual-band index.</summary>
    internal BandIndex<int> Bands { get; }
}
