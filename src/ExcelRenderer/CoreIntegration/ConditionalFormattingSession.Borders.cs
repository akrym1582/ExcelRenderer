using ExcelRenderer.Core.Model;
using CellAddress = ExcelRenderer.Core.Model.CellAddress;
using SourceAddress = ExcelRenderer.OpenXml.Reader.Model.Cells.CellAddress;
using SourceRange = ExcelRenderer.OpenXml.Reader.Model.Cells.CellRange;

namespace ExcelRenderer.CoreIntegration;

/// <summary>Applies final differential borders to saved owners just outside selected content.</summary>
internal sealed partial class ConditionalFormattingSession
{
    private HashSet<CellAddress>? AddSavedBorderNeighbors(
        Dictionary<CellAddress, ReportCell> cells,
        HashSet<CellAddress> candidates,
        Core.Layout.BandIndex<SourceRange> merges,
        long candidateLimit,
        ref long visits,
        CancellationToken token)
    {
        var result = new HashSet<CellAddress>();
        if (!rules.Any(rule => rule.Style.HasBorders))
        {
            return result;
        }

        var hiddenRows = info.Rows.ToDictionary(row => row.Index, row => row.Hidden ?? info.DefaultRowsHidden);
        var hiddenColumns = new bool[16385];
        foreach (var column in info.Columns)
        {
            for (var index = column.Min; index <= column.Max; index++)
            {
                hiddenColumns[index] = column.Hidden ?? false;
            }
        }

        foreach (var address in cells.Keys)
        {
            token.ThrowIfCancellationRequested();
            var source = new SourceAddress(address.Row, address.Column);
            if (!Visible(source) || isTarget(source) ||
                !index.Query(address.Row, address.Row + 1).Any(entry => entry.Rule.Style.HasBorders && address.Column >= entry.Range.First.Column && address.Column <= entry.Range.Last.Column))
            {
                continue;
            }

            var merge = Merge(source);
            if (merge is not null && !merge.First.Equals(source))
            {
                continue;
            }

            if (Target(address.Row - 1, address.Column) || Target(address.Row + 1, address.Column) ||
                Target(address.Row, address.Column - 1) || Target(address.Row, address.Column + 1))
            {
                result.Add(address);
                if (candidates.Add(address) && ++visits > candidateLimit)
                {
                    ReportCandidateLimit(rules.First(rule => rule.Style.HasBorders), candidates, "The finite border-neighbor candidate budget was exceeded; this sheet retains ordinary formatting.");
                    return null;
                }
            }
        }

        return result;

        bool Visible(SourceAddress address) => !hiddenColumns[address.Column] && !hiddenRows.GetValueOrDefault(address.Row, info.DefaultRowsHidden);

        SourceRange? Merge(SourceAddress address) => merges.Query(address.Row, address.Row + 1)
            .FirstOrDefault(range => address.Column >= range.First.Column && address.Column <= range.Last.Column);

        bool Target(int row, int column)
        {
            if (row < 1 || row > 1048576 || column < 1 || column > 16384)
            {
                return false;
            }

            var address = new SourceAddress(row, column);
            return Visible(address) && isTarget(Merge(address)?.First ?? address);
        }
    }
}
