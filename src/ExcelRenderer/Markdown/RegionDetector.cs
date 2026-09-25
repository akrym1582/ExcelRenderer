using ExcelRenderer.Model;

namespace ExcelRenderer.Markdown;

/// <summary>
/// RegionDetector が表すデータと操作を提供します.
/// </summary>
public sealed class RegionDetector
{
    /// <summary>
    /// Detect を実行します.
    /// </summary>
    /// <param name="root">root に渡す値です。</param>
    /// <param name="sheet">sheet に渡す値です。</param>
    /// <returns>処理によって得られた結果を返します。</returns>
    public IReadOnlyList<SheetRegion> Detect(LayoutNode root, ReportSheet sheet)
    {
        var leaves = Flatten(root).Where(x => x.Cells.Count > 0).ToArray();
        return leaves.Select(node => Create(node.Cells, sheet)).ToArray();
    }

    private static IEnumerable<LayoutNode> Flatten(LayoutNode node) => node.Children.Count == 0
        ? new[] { node }
        : node.Children.SelectMany(Flatten);

    private static SheetRegion Create(IReadOnlyList<VisualCell> cells, ReportSheet sheet)
    {
        var first = new CellAddress(cells.Min(c => c.Range.First.Row), cells.Min(c => c.Range.First.Column));
        var last = new CellAddress(cells.Max(c => c.Range.Last.Row), cells.Max(c => c.Range.Last.Column));
        var bounds = LayoutSegmenter.Bounds(cells);
        var images = (sheet.Images ?? Array.Empty<ReportImage>()).Where(i =>
        {
            var x = VisualCellBuilder.OffsetX(sheet, i.Anchor.Column) + i.OffsetX + (i.Width / 2);
            var y = VisualCellBuilder.OffsetY(sheet, i.Anchor.Row) + i.OffsetY + (i.Height / 2);
            return bounds.Contains(x, y);
        }).ToArray();
        return new()
        {
            BoundingRange = new(first, last), BoundingBox = bounds, Cells = cells,
            Images = images, Type = new RegionClassifier().Classify(cells),
        };
    }
}
