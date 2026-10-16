using ExcelRenderer.Core.Model;

namespace ExcelRenderer.Core.Excel;

/// <summary>Shared model assembly algorithms for both workbook adapters.</summary>
internal static class WorkbookModelBuilder
{
    /// <summary>Materializes default dimensions for the durable public Reader dictionaries.</summary>
    /// <param name="sheet">The sparse source model.</param>
    /// <param name="additionalGeometry">Product object anchors and other finite source regions.</param>
    /// <returns>A model with dimensions through each used or geometrical range.</returns>
    internal static ReportSheet WithMaterializedDimensions(ReportSheet sheet, IReadOnlyList<CellRange>? additionalGeometry = null)
    {
        var ranges = sheet.MergedRanges.Concat(sheet.PrintAreas).Concat(sheet.Cells.Keys.Select(address => new CellRange(address, address)))
            .Concat((sheet.Images ?? []).Select(image => new CellRange(
                image.DrawingAnchor?.From ?? image.Anchor,
                image.DrawingAnchor?.To ?? image.DrawingAnchor?.From ?? image.Anchor)))
            .Concat(additionalGeometry ?? []).ToArray();
        var columns = sheet.Columns.ToDictionary(pair => pair.Key, pair => pair.Value);
        var rows = sheet.Rows.ToDictionary(pair => pair.Key, pair => pair.Value);
        if (ranges.Length > 0)
        {
            var firstColumn = ranges.Min(range => Math.Min(range.First.Column, range.Last.Column));
            var lastColumn = ranges.Max(range => Math.Max(range.First.Column, range.Last.Column));
            var firstRow = ranges.Min(range => Math.Min(range.First.Row, range.Last.Row));
            var lastRow = ranges.Max(range => Math.Max(range.First.Row, range.Last.Row));
            for (var column = firstColumn; column <= lastColumn; column++)
            {
                if (!columns.ContainsKey(column))
                {
                    columns[column] = new(sheet.DefaultColumnWidth);
                }
            }

            for (var row = firstRow; row <= lastRow; row++)
            {
                if (!rows.ContainsKey(row))
                {
                    rows[row] = new(sheet.DefaultRowHeight, sheet.DefaultRowsHidden);
                }
            }
        }

        return sheet with { Columns = columns, Rows = rows };
    }

    /// <summary>Completes merged leaders and gathers visible perimeter borders from the shared model.</summary>
    /// <param name="cells">The sparse neutral cell model.</param>
    /// <param name="ranges">Source merge geometry.</param>
    /// <param name="styles">Conversion-local style pool.</param>
    /// <returns>The same model after merging.</returns>
    internal static Dictionary<CellAddress, ReportCell> ApplyMergedSpans(
        Dictionary<CellAddress, ReportCell> cells,
        IEnumerable<CellRange> ranges,
        StylePool styles)
    {
        var merged = ranges.ToArray();
        var index = new CellRangeIndex(cells.Keys.Concat(merged.Select(range => range.First)).Distinct());
        foreach (var range in merged)
        {
            var entries = index.Query(range).Where(cells.ContainsKey)
                .Select(address => new KeyValuePair<CellAddress, ReportCell>(address, cells[address])).ToArray();
            var cell = cells.GetValueOrDefault(range.First, new(null, CellStyle.Default));
            cells[range.First] = cell with
            {
                RowSpan = range.Last.Row - range.First.Row + 1,
                ColumnSpan = range.Last.Column - range.First.Column + 1,
                Style = styles.Intern(cell.Style with { Border = null }),
                MergedBorders = ReadMergedBorders(entries, range, styles),
            };
            foreach (var address in entries.Select(entry => entry.Key).Where(address => address != range.First))
            {
                cells.Remove(address);
            }
        }

        return cells;
    }

    /// <summary>Materializes only painted default-style blanks within finite rendering regions.</summary>
    /// <param name="rows">Sparse custom row style IDs.</param>
    /// <param name="columns">Bounded source column style IDs, indexed one-based.</param>
    /// <param name="regions">Explicit, printed or existing-used rectangles.</param>
    /// <param name="cells">The final rendering cells.</param>
    /// <param name="resolve">Conversion-local blank-style resolution.</param>
    internal static void AddStyledBlanks(IReadOnlyDictionary<int, int> rows, IReadOnlyList<int?> columns, IReadOnlyList<CellRange> regions, Dictionary<CellAddress, ReportCell> cells, Func<int, CellStyle> resolve)
    {
        var cache = new Dictionary<int, CellStyle>();
        var defaultPaint = Paint(Style(0));
        foreach (var region in regions)
        {
            if (defaultPaint)
            {
                for (var row = region.First.Row; row <= region.Last.Row; row++)
                {
                    for (var column = region.First.Column; column <= region.Last.Column; column++)
                    {
                        Add(row, column);
                    }
                }

                continue;
            }

            foreach (var row in rows.Where(row => row.Key >= region.First.Row && row.Key <= region.Last.Row && Paint(Style(row.Value))))
            {
                for (var column = region.First.Column; column <= region.Last.Column; column++)
                {
                    Add(row.Key, column);
                }
            }

            for (var column = region.First.Column; column <= region.Last.Column; column++)
            {
                if (columns[column] is not { } index || !Paint(Style(index)))
                {
                    continue;
                }

                for (var row = region.First.Row; row <= region.Last.Row; row++)
                {
                    Add(row, column);
                }
            }
        }

        static bool Paint(CellStyle style) => style.Background is not null || style.Border is not null;

        CellStyle Style(int index)
        {
            if (!cache.TryGetValue(index, out var style))
            {
                style = resolve(index);
                cache.Add(index, style);
            }

            return style;
        }

        void Add(int row, int column)
        {
            var address = new CellAddress(row, column);
            if (cells.ContainsKey(address))
            {
                return;
            }

            var index = rows.TryGetValue(row, out var rowStyle) ? rowStyle : columns[column] ?? 0;
            var style = Style(index);
            if (Paint(style))
            {
                cells.Add(address, new(string.Empty, style));
            }
        }
    }

    private static IReadOnlyList<CellBorder> ReadMergedBorders(IEnumerable<KeyValuePair<CellAddress, ReportCell>> entries, CellRange range, StylePool styles)
    {
        var borders = new List<CellBorder>();
        foreach (var entry in entries)
        {
            var source = entry.Value.Style.Border;
            if (source is null)
            {
                continue;
            }

            var address = entry.Key;
            var border = new BorderStyle(
                address.Column == range.First.Column ? source.Left : null,
                address.Row == range.First.Row ? source.Top : null,
                address.Column == range.Last.Column ? source.Right : null,
                address.Row == range.Last.Row ? source.Bottom : null);
            if (border != new BorderStyle())
            {
                borders.Add(new(address, styles.Intern(border)));
            }
        }

        return borders;
    }
}
