using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using ExcelRenderer.Rendering;

namespace ExcelRenderer;

/// <summary>Projects drawing models after full selected-sheet glyph diagnostics.</summary>
public static partial class ExcelConverter
{
    private static ReportSheet ProjectSheet(ReportSheet sheet, RenderRequest request)
    {
        var used = ResolvePrintAreaPass.GetUsedRange(sheet);
        var ranges = sheet.RequestedRange is { } requested ? new[] { requested }
            : request.ImageLayout == ImageLayoutMode.Continuous ? Array.Empty<CellRange>()
            : sheet.PrintAreas.Count > 0 ? sheet.PrintAreas.ToArray()
            : sheet.PrintArea is { } printArea ? new[] { printArea } : Array.Empty<CellRange>();
        if (ranges.Length == 0)
        {
            return sheet with { RenderUsedRange = used };
        }

        var settings = sheet.PageSettings;
        bool Keep(CellAddress address, ReportCell cell)
        {
            var lastRow = address.Row + cell.RowSpan - 1;
            var lastColumn = address.Column + cell.ColumnSpan - 1;
            var titleRow = request.ImageLayout != ImageLayoutMode.Continuous && settings.TitleRows is { } rows && address.Row >= rows.First && address.Row <= rows.Last;
            var titleColumn = request.ImageLayout != ImageLayoutMode.Continuous && settings.TitleColumns is { } columns && address.Column >= columns.First && address.Column <= columns.Last;
            return ranges.Any(range =>
                ((address.Row <= range.Last.Row && lastRow >= range.First.Row) || titleRow) &&
                ((address.Column <= range.Last.Column && lastColumn >= range.First.Column) || titleColumn));
        }

        var geometry = new SheetGeometry(sheet);
        var rectangles = ranges.Select(range => RectangleGeometry.Bounds(geometry, range)).ToArray();
        bool Intersects(ReportRect bounds) => rectangles.Any(rectangle => RectangleGeometry.Intersect(bounds, rectangle) is not null);
        return sheet with
        {
            RenderUsedRange = used,
            Images = (sheet.Images ?? []).Where(image => Intersects(ObjectGeometry.GetVisualBounds(
                ObjectGeometry.GetSheetRect(geometry, image.Anchor, image.OffsetX, image.OffsetY, image.Width, image.Height, image.DrawingAnchor), image.Rotation))).ToArray(),
            Shapes = (sheet.Shapes ?? []).Where(shape => Intersects(ObjectGeometry.GetShapeVisualBounds(
                ObjectGeometry.GetSheetRect(geometry, shape.Anchor, shape.OffsetX, shape.OffsetY, shape.Width, shape.Height, shape.DrawingAnchor), shape))).ToArray(),
            Cells = sheet.Cells.Where(pair => Keep(pair.Key, pair.Value)).ToDictionary(pair => pair.Key, pair => pair.Value),
        };
    }
}
