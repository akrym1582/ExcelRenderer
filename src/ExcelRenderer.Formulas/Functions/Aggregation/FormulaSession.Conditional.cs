using System;
using System.Collections.Generic;

namespace ExcelRenderer.Formulas
{
    /// <summary>Implements bounded session evaluation for this function family.</summary>
    public sealed partial class FormulaSession
    {
        private static bool SameShape(FormulaRange a, FormulaRange b) => Width(a) == Width(b) && a.LastRow - a.FirstRow == b.LastRow - b.FirstRow;

        private FormulaValue ConditionalAggregate(string name, FormulaNode node, FormulaCellAddress origin)
        {
            bool sum = name == "SUMIF" || name == "SUMIFS";
            bool single = name == "SUMIF" || name == "COUNTIF";
            var args = node.Children;
            var ranges = new List<FormulaRange>();
            var conditions = new List<CriteriaMatcher>();
            FormulaRange sumRange = default;
            if (name == "SUMIFS")
            {
                var sumItem = Eval(args[0], origin);
                if (!sumItem.IsReference)
                {
                    return sumItem.Value.Kind == FormulaValueKind.Error ? sumItem.Value : FormulaValue.FromError("#VALUE!");
                }

                sumRange = sumItem.Range;
            }

            for (var i = name == "SUMIFS" ? 1 : 0; i < (single ? 2 : args.Count); i += 2)
            {
                var item = Eval(args[i], origin);
                if (!item.IsReference)
                {
                    return item.Value.Kind == FormulaValueKind.Error ? item.Value : FormulaValue.FromError("#VALUE!");
                }

                ranges.Add(item.Range);
                var criterion = Scalar(Eval(args[i + 1], origin), origin);
                if (criterion.Kind == FormulaValueKind.Error)
                {
                    return criterion;
                }

                conditions.Add(new CriteriaMatcher(criterion));
            }

            var shape = ranges[0];
            if (name == "SUMIF")
            {
                sumRange = shape;
                if (args.Count == 3)
                {
                    var item = Eval(args[2], origin);
                    if (!item.IsReference)
                    {
                        return item.Value.Kind == FormulaValueKind.Error ? item.Value : FormulaValue.FromError("#VALUE!");
                    }

                    var r = item.Range;
                    long lastRow = (long)r.FirstRow + shape.LastRow - shape.FirstRow;
                    long lastColumn = r.FirstColumn + Width(shape) - 1;
                    if (lastRow > 1048576 || lastColumn > 16384)
                    {
                        return FormulaValue.FromError("#REF!");
                    }

                    sumRange = new FormulaRange(r.SheetId, r.FirstRow, r.FirstColumn, (int)lastRow, (int)lastColumn);
                }
            }

            foreach (var range in ranges)
            {
                if (!SameShape(shape, range))
                {
                    return FormulaValue.FromError("#VALUE!");
                }
            }

            if (sum && !SameShape(shape, sumRange))
            {
                return FormulaValue.FromError("#VALUE!");
            }

            if (sum)
            {
                ranges.Add(sumRange);
            }

            Action tick = () => Tick(origin, node.Span);
            bool blankMatch = true;
            foreach (var condition in conditions)
            {
                blankMatch &= condition.Matches(FormulaValue.Blank, tick);
            }

            var cursors = new List<IEnumerator<FormulaCellAddress>>();
            var positions = new long[ranges.Count];
            double result = 0;
            long nextPosition = 0;
            FormulaValue? selectedError = null;
            try
            {
                for (var i = 0; i < ranges.Count; i++)
                {
                    var cursor = Coordinates(ranges[i], origin, node.Span).GetEnumerator();
                    cursors.Add(cursor);
                    positions[i] = cursor.MoveNext() ? Position(ranges[i], cursor.Current) : long.MaxValue;
                }

                while (true)
                {
                    tick();
                    var position = long.MaxValue;
                    for (var i = 0; i < positions.Length; i++)
                    {
                        tick();
                        position = Math.Min(position, positions[i]);
                    }

                    var end = position == long.MaxValue ? Size(shape) : position;
                    if (end > nextPosition)
                    {
                        bulkBlankCells += end - nextPosition;
                        if (!sum && blankMatch)
                        {
                            result += end - nextPosition;
                        }
                    }

                    if (position == long.MaxValue)
                    {
                        break;
                    }

                    bool match = true;

                    // Read every criteria candidate even after a mismatch: engine failures are never hidden.
                    for (var i = 0; i < conditions.Count; i++)
                    {
                        var value = positions[i] == position ? Read(cursors[i].Current, origin, node.Span) : FormulaValue.Blank;
                        match &= conditions[i].Matches(value, tick);
                    }

                    if (match)
                    {
                        if (sum)
                        {
                            var i = ranges.Count - 1;
                            var value = positions[i] == position ? Read(cursors[i].Current, origin, node.Span) : FormulaValue.Blank;
                            if (value.Kind == FormulaValueKind.Number)
                            {
                                result += value.Number;
                            }
                            else if (value.Kind == FormulaValueKind.Error)
                            {
                                selectedError ??= value;
                            }
                        }
                        else
                        {
                            result++;
                        }
                    }

                    nextPosition = position + 1;
                    for (var i = 0; i < positions.Length; i++)
                    {
                        tick();
                        if (positions[i] == position)
                        {
                            positions[i] = cursors[i].MoveNext() ? Position(ranges[i], cursors[i].Current) : long.MaxValue;
                        }
                    }
                }
            }
            finally
            {
                foreach (var cursor in cursors)
                {
                    cursor.Dispose();
                }
            }

            return selectedError ?? Number(result);
        }
    }
}
