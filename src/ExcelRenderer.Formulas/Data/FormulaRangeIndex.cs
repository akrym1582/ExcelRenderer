using System;
using System.Collections;
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
                var coverage = new Dictionary<int, (int First, int Last, int Count)>();
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
                        coverage.TryGetValue(coordinate.Column, out var previous);
                        coverage[coordinate.Column] = (previous.Count == 0 ? coordinate.Row : previous.First, coordinate.Row, previous.Count + 1);
                    }

                    lastRow = coordinate.Row;
                    lastColumn = coordinate.Column;
                }

                offsets.Add(columns.Count);
                sheets.Add(sheet.Key, new SheetIndex(rows.ToArray(), offsets.ToArray(), columns.ToArray(), coverage));
            }
        }

        /// <summary>Streams coordinates using binary searches on the row and column indexes.</summary>
        /// <param name="range">Inclusive rectangle.</param>
        /// <param name="cancellationToken">Cooperative cancellation.</param>
        /// <returns>Stored coordinates in row order.</returns>
        public IEnumerable<FormulaCellAddress> Enumerate(FormulaRange range, CancellationToken cancellationToken = default) => new Query(this, range, cancellationToken);

        private static int LowerBound(int[] values, int target)
        {
            var position = Array.BinarySearch(values, target);
            return position < 0 ? ~position : position;
        }

        private bool CoversDensely(FormulaRange range, CancellationToken cancellationToken)
        {
            if (!sheets.TryGetValue(range.SheetId, out var sheet))
            {
                return false;
            }

            if (range.FirstRow == range.LastRow && range.FirstColumn != range.LastColumn)
            {
                var row = Array.BinarySearch(sheet.Rows, range.FirstRow);
                if (row < 0)
                {
                    return false;
                }

                int start = sheet.Offsets[row];
                int end = sheet.Offsets[row + 1];
                return end - start == sheet.Columns[end - 1] - sheet.Columns[start] + 1 && range.FirstColumn >= sheet.Columns[start] && range.LastColumn <= sheet.Columns[end - 1];
            }

            for (int column = range.FirstColumn; column <= range.LastColumn; column++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!sheet.Coverage.TryGetValue(column, out var coverage) || coverage.Count != coverage.Last - coverage.First + 1 || range.FirstRow < coverage.First || range.LastRow > coverage.Last)
                {
                    return false;
                }
            }

            return true;
        }

        private IEnumerable<FormulaCellAddress> EnumerateCore(FormulaRange range, CancellationToken cancellationToken)
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
                var found = sheet.Columns[start] >= range.FirstColumn ? start : Array.BinarySearch(sheet.Columns, start, end - start, range.FirstColumn);
                for (var j = found < 0 ? ~found : found; j < end && sheet.Columns[j] <= range.LastColumn; j++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return new FormulaCellAddress(range.SheetId, sheet.Rows[i], sheet.Columns[j]);
                }
            }
        }

        /// <summary>Identifies a checked index query without trusting arbitrary sparse enumerables.</summary>
        internal sealed class Query : IEnumerable<FormulaCellAddress>
        {
            private readonly FormulaRangeIndex owner;
            private readonly FormulaRange range;
            private readonly CancellationToken cancellation;

            /// <summary>Initializes a new instance of the <see cref="Query"/> class.</summary>
            /// <param name="owner">Validated coordinate index.</param>
            /// <param name="range">Requested rectangle.</param>
            /// <param name="cancellation">Cooperative cancellation.</param>
            internal Query(FormulaRangeIndex owner, FormulaRange range, CancellationToken cancellation)
            {
                this.owner = owner;
                this.range = range;
                this.cancellation = cancellation;
            }

            /// <inheritdoc/>
            public IEnumerator<FormulaCellAddress> GetEnumerator() => owner.EnumerateCore(range, cancellation).GetEnumerator();

            /// <inheritdoc/>
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            /// <summary>Proves all coordinates exist for exactly the requested rectangle.</summary>
            /// <param name="requested">Session request, which must match the query.</param>
            /// <returns>Whether direct coordinate iteration is equivalent.</returns>
            internal bool CoversDensely(FormulaRange requested) => StringComparer.OrdinalIgnoreCase.Equals(range.SheetId, requested.SheetId) && range.FirstRow == requested.FirstRow && range.FirstColumn == requested.FirstColumn && range.LastRow == requested.LastRow && range.LastColumn == requested.LastColumn && owner.CoversDensely(range, cancellation);
        }

        private sealed class SheetIndex
        {
            internal SheetIndex(int[] rows, int[] offsets, int[] columns, Dictionary<int, (int First, int Last, int Count)> coverage)
            {
                Rows = rows;
                Offsets = offsets;
                Columns = columns;
                Coverage = coverage;
            }

            internal int[] Rows { get; }

            internal int[] Offsets { get; }

            internal int[] Columns { get; }

            internal Dictionary<int, (int First, int Last, int Count)> Coverage { get; }
        }
    }
}
