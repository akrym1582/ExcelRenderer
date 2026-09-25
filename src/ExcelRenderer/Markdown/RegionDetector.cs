using ExcelRenderer.Model;

namespace ExcelRenderer.Markdown;

/// <summary>
/// レイアウトツリーの末端ノードから、セルと画像を含むワークシート領域を検出します。
/// </summary>
public sealed class RegionDetector
{
    /// <summary>
    /// セルを持つ末端ノードごとに範囲と領域種別を求め、領域内に配置された画像を関連付けます。
    /// </summary>
    /// <param name="root">検出元となるレイアウトツリーのルートノード。</param>
    /// <param name="sheet">画像の位置情報を取得するワークシート。</param>
    /// <returns>レイアウトツリーの末端ノード順に作成した、空でないワークシート領域の一覧。</returns>
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
