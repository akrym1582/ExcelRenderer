using System;
using System.Collections.Generic;

namespace ExcelRenderer.Formulas
{
    /// <summary>Implements bounded session evaluation for this function family.</summary>
    public sealed partial class FormulaSession
    {
        private static long Width(FormulaRange range) => (long)range.LastColumn - range.FirstColumn + 1;

        private static long Size(FormulaRange range) => Width(range) * ((long)range.LastRow - range.FirstRow + 1);

        private static long Position(FormulaRange range, FormulaCellAddress address) => (((long)address.Row - range.FirstRow) * Width(range)) + address.Column - range.FirstColumn;

        private static FormulaCellAddress Address(FormulaRange range, long position) => new FormulaCellAddress(range.SheetId, range.FirstRow + (int)(position / Width(range)), range.FirstColumn + (int)(position % Width(range)));

        private IEnumerable<FormulaCellAddress> Coordinates(FormulaRange range, FormulaCellAddress origin, SourceSpan span)
        {
            if (!(source is ISparseFormulaDataSource sparse))
            {
                for (var row = range.FirstRow; row <= range.LastRow; row++)
                {
                    for (var col = range.FirstColumn; col <= range.LastColumn; col++)
                    {
                        Tick(origin, span);
                        rangeCandidates++;
                        yield return new FormulaCellAddress(range.SheetId, row, col);
                    }
                }

                yield break;
            }

            IEnumerator<FormulaCellAddress>? cursor = null;
            try
            {
                try
                {
                    cursor = sparse.EnumerateStoredCells(range, cancellation).GetEnumerator();
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Fail("DataSourceFailure", ex.Message, origin, span);
                }

                long previous = -1;
                while (true)
                {
                    Tick(origin, span);
                    bool moved = false;
                    FormulaCellAddress address = default;
                    try
                    {
                        moved = cursor!.MoveNext();
                        if (moved)
                        {
                            address = cursor.Current;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Fail("DataSourceFailure", ex.Message, origin, span);
                    }

                    if (!moved)
                    {
                        yield break;
                    }

                    var position = Position(range, address);
                    if (!StringComparer.OrdinalIgnoreCase.Equals(range.SheetId, address.SheetId) || address.Row < range.FirstRow || address.Row > range.LastRow || address.Column < range.FirstColumn || address.Column > range.LastColumn || position <= previous)
                    {
                        Fail("DataSourceFailure", "Sparse coordinates must be in range, unique and ordered by row then column.", origin, span);
                    }

                    previous = position;
                    rangeCandidates++;
                    yield return address;
                }
            }
            finally
            {
                try
                {
                    cursor?.Dispose();
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Fail("DataSourceFailure", ex.Message, origin, span);
                }
            }
        }
    }
}
