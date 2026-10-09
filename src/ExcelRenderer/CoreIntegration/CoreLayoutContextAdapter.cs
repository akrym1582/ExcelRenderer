using ExcelRenderer.Abstractions;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;

namespace ExcelRenderer.CoreIntegration;

/// <summary>Synchronizes only compatibility pass state; it never runs a replacement engine.</summary>
internal static class CoreLayoutContextAdapter
{
    /// <summary>Projects common geometry and the selected cell dictionary without copying all cells.</summary>
    /// <param name="context">The independent public pass context.</param>
    /// <returns>The common geometry context.</returns>
    internal static Core.Layout.ReportLayoutContext CreateGeometry(ReportLayoutContext context)
    {
        var result = new Core.Layout.ReportLayoutContext(CoreModelAdapter.ToCore(context.Sheet), CoreTextMeasurerAdapter.Create(context.TextMeasurer), context.Geometry.CoreGeometry)
        {
            Policy = new FullLayoutPolicy(),
            PrintArea = context.PrintArea is { } area ? CoreModelAdapter.ToCore(area) : null,
            VisibleColumns = context.VisibleColumns,
            VisibleRows = context.VisibleRows,
            CancellationToken = context.CancellationToken,
            CandidateAddresses = context.CandidateAddresses?.Select(CoreModelAdapter.ToCore).ToArray(),
            ObjectLayouts = context.ObjectLayouts?.CoreIndex,
        };
        foreach (var column in context.ColumnLayouts)
        {
            result.ColumnLayouts[column.Key] = new(column.Value.Column, column.Value.X, column.Value.Width);
        }

        foreach (var row in context.RowLayouts)
        {
            result.RowLayouts[row.Key] = new(row.Value.Row, row.Value.Y, row.Value.Height);
        }

        return result;
    }

    /// <summary>Projects the already measured page state for bounds or public pagination.</summary>
    /// <param name="context">The public measured context.</param>
    /// <returns>The page-local common context.</returns>
    internal static Core.Layout.ReportLayoutContext CreateMeasured(ReportLayoutContext context)
    {
        var result = CreateGeometry(context);
        foreach (var size in context.TextSizes)
        {
            result.TextSizes[CoreModelAdapter.ToCore(size.Key)] = new(size.Value.Width, size.Value.Height);
        }

        foreach (var layout in context.TextLayouts)
        {
            result.TextLayouts[CoreModelAdapter.ToCore(layout.Key)] = CoreTextLayoutAdapter.ToCore(layout.Value);
        }

        return result;
    }

    /// <summary>Projects already placed cells for independent public pagination.</summary>
    /// <param name="context">The public page context.</param>
    /// <returns>The common page context.</returns>
    internal static Core.Layout.ReportLayoutContext CreatePage(ReportLayoutContext context)
    {
        var result = CreateMeasured(context);
        foreach (var cell in context.CellLayouts)
        {
            var layout = cell.Value;
            result.CellLayouts[CoreModelAdapter.ToCore(cell.Key)] = new(CoreModelAdapter.ToCore(layout.Address), CoreModelAdapter.ToCore(layout.Bounds), new(layout.TextSize.Width, layout.TextSize.Height))
            {
                ContentBounds = CoreModelAdapter.ToCore(layout.ContentBounds),
                MergedBorders = layout.MergedBorders?.Select(border => new Core.Layout.RenderBorder(CoreModelAdapter.ToCore(border.Bounds), CoreModelAdapter.ToCore(border.Border))).ToArray(),
            };
        }

        return result;
    }

    /// <summary>Synchronizes measured results without changing unrelated public context state.</summary>
    /// <param name="source">The executed common pass context.</param>
    /// <param name="target">The original public context.</param>
    internal static void SyncMeasurements(Core.Layout.ReportLayoutContext source, ReportLayoutContext target)
    {
        foreach (var size in source.TextSizes)
        {
            target.TextSizes[CoreModelAdapter.ToPublic(size.Key)] = new(size.Value.Width, size.Value.Height);
        }

        foreach (var layout in source.TextLayouts)
        {
            target.TextLayouts[CoreModelAdapter.ToPublic(layout.Key)] = CoreTextLayoutAdapter.ToPublic(layout.Value);
        }
    }

    /// <summary>Synchronizes cell bounds without replacing caller-owned dictionary instances.</summary>
    /// <param name="source">The executed common bounds pass context.</param>
    /// <param name="target">The original public context.</param>
    internal static void SyncCellBounds(Core.Layout.ReportLayoutContext source, ReportLayoutContext target)
    {
        foreach (var cell in source.CellLayouts)
        {
            var layout = cell.Value;
            target.CellLayouts[CoreModelAdapter.ToPublic(cell.Key)] = new(CoreModelAdapter.ToPublic(layout.Address), CoreModelAdapter.ToPublic(layout.Bounds), new(layout.TextSize.Width, layout.TextSize.Height))
            {
                ContentBounds = CoreModelAdapter.ToPublic(layout.ContentBounds),
                MergedBorders = layout.MergedBorders?.Select(border => new RenderBorder(CoreModelAdapter.ToPublic(border.Bounds), CoreModelAdapter.ToPublic(border.Border))).ToArray(),
            };
        }
    }
}
