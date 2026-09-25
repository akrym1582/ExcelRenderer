using ExcelRenderer.Model;

namespace ExcelRenderer.Markdown;

/// <summary>
/// RegionClassifier が表すデータと操作を提供します.
/// </summary>
public sealed class RegionClassifier
{
    /// <summary>
    /// Classify を実行します.
    /// </summary>
    /// <param name="cells">cells に渡す値です。</param>
    /// <returns>処理によって得られた結果を返します。</returns>
    public RegionType Classify(IReadOnlyList<VisualCell> cells)
    {
        var nonEmpty = cells.Where(c => !string.IsNullOrWhiteSpace(c.Text)).ToArray();
        if (nonEmpty.Length == 0)
        {
            return RegionType.FreeLayout;
        }

        if (nonEmpty.Length == 1 && (nonEmpty[0].Range.Last.Column > nonEmpty[0].Range.First.Column ||
            nonEmpty[0].Style.Font.Bold || nonEmpty[0].Style.Font.Size >= 14))
        {
            return RegionType.Title;
        }

        var rows = nonEmpty.GroupBy(c => c.Range.First.Row).ToArray();
        if (rows.Length >= 2 && rows.All(r => r.Count() == 2))
        {
            return RegionType.Form;
        }

        if (rows.Length >= 2 && rows.Select(r => r.Count()).Distinct().Count() == 1)
        {
            return RegionType.Table;
        }

        return nonEmpty.Length == 1 ? RegionType.Text : RegionType.FreeLayout;
    }
}
