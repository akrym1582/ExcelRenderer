using System;
using System.Collections.Generic;
using System.Threading;

namespace ExcelRenderer.Formulas
{
    /// <summary>Indexes immutable stored coordinates by sheet, sorted row and sorted column.</summary>
    public sealed class FormulaRangeIndex
    {
        private readonly Dictionary<string, SheetIndex> sheets = new Dictionary<string, SheetIndex>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Initializes a new instance of the <see cref="FormulaRangeIndex"/> class.</summary>
        /// <param name="addresses">All stored coordinates.</param>
        /// <param name="cancellationToken">Cooperative cancellation.</param>
        public FormulaRangeIndex(IEnumerable<FormulaCellAddress> addresses, CancellationToken cancellationToken = default)
        {
            if (addresses == null)
            {
                throw new ArgumentNullException(nameof(addresses));
            }

            var building = new Dictionary<string, List<(int Row, int Column)>>(StringComparer.OrdinalIgnoreCase);
            foreach (var address in addresses)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!building.TryGetValue(address.SheetId, out var coordinates))
                {
                    coordinates = new List<(int Row, int Column)>();
                    building.Add(address.SheetId, coordinates);
                }

                coordinates.Add((address.Row, address.Column));
            }

            foreach (var sheet in building)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sheet.Value.Sort((a, b) => a.Row == b.Row ? a.Column.CompareTo(b.Column) : a.Row.CompareTo(b.Row));
                var rows = new List<int>();
                var offsets = new List<int>();
                var columns = new List<int>(sheet.Value.Count);
                int lastRow = -1;
                int lastColumn = -1;
                foreach (var coordinate in sheet.Value)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (coordinate.Row != lastRow)
                    {
                        rows.Add(coordinate.Row);
                        offsets.Add(columns.Count);
                        lastColumn = -1;
                    }

                    if (coordinate.Row != lastRow || coordinate.Column != lastColumn)
                    {
                        columns.Add(coordinate.Column);
                    }

                    lastRow = coordinate.Row;
                    lastColumn = coordinate.Column;
                }

                offsets.Add(columns.Count);
                sheets.Add(sheet.Key, new SheetIndex(rows.ToArray(), offsets.ToArray(), columns.ToArray()));
            }
        }

        /// <summary>Streams coordinates using binary searches on the row and column indexes.</summary>
        /// <param name="range">Inclusive rectangle.</param>
        /// <param name="cancellationToken">Cooperative cancellation.</param>
        /// <returns>Stored coordinates in row order.</returns>
        public IEnumerable<FormulaCellAddress> Enumerate(FormulaRange range, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!sheets.TryGetValue(range.SheetId, out var sheet))
            {
                yield break;
            }

            for (var i = LowerBound(sheet.Rows, range.FirstRow); i < sheet.Rows.Length && sheet.Rows[i] <= range.LastRow; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var start = sheet.Offsets[i];
                var end = sheet.Offsets[i + 1];
                var found = Array.BinarySearch(sheet.Columns, start, end - start, range.FirstColumn);
                for (var j = found < 0 ? ~found : found; j < end && sheet.Columns[j] <= range.LastColumn; j++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return new FormulaCellAddress(range.SheetId, sheet.Rows[i], sheet.Columns[j]);
                }
            }
        }

        private static int LowerBound(int[] values, int target)
        {
            var position = Array.BinarySearch(values, target);
            return position < 0 ? ~position : position;
        }

        private sealed class SheetIndex
        {
            internal SheetIndex(int[] rows, int[] offsets, int[] columns)
            {
                Rows = rows;
                Offsets = offsets;
                Columns = columns;
            }

            internal int[] Rows { get; }

            internal int[] Offsets { get; }

            internal int[] Columns { get; }
        }
    }
}
