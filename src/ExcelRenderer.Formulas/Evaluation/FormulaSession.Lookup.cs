using System;
using ExcelRenderer.Formulas.Criteria;
using ExcelRenderer.Formulas.References;
using ExcelRenderer.Formulas.Syntax;
using ExcelRenderer.Formulas.Values;

namespace ExcelRenderer.Formulas.Evaluation
{
    /// <summary>Implements bounded session evaluation for this function family.</summary>
    public sealed partial class FormulaSession
    {
        private static bool LookupMatch(FormulaValue key, FormulaValue value, WildcardMatcher? wildcard, Action tick)
        {
            tick();
            if (value.Kind == FormulaValueKind.Blank)
            {
                return key.Kind == FormulaValueKind.Number && key.Number == 0;
            }

            if (key.Kind != value.Kind)
            {
                return false;
            }

            return key.Kind == FormulaValueKind.Text ? (wildcard == null ? StringComparer.OrdinalIgnoreCase.Equals(key.Text, value.Text) : wildcard.Matches(value.Text, tick)) : key.Kind == FormulaValueKind.Number ? key.Number == value.Number : key.Boolean == value.Boolean;
        }

        private Item LookupFunction(string name, FormulaNode node, FormulaCellAddress origin)
        {
            var args = node.Children;
            if (name == "INDEX")
            {
                return Index(node, origin);
            }

            var key = Scalar(Eval(args[0], origin), origin);
            if (key.Kind == FormulaValueKind.Error)
            {
                return new Item(key);
            }

            if (key.Kind == FormulaValueKind.Blank)
            {
                key = Number(0);
            }

            var table = Eval(args[1], origin);
            if (!table.IsReference)
            {
                return new Item(table.Value.Kind == FormulaValueKind.Error ? table.Value : FormulaValue.FromError("#VALUE!"));
            }

            var range = table.Range;
            double column = 1;
            if (name == "VLOOKUP")
            {
                var col = Numeric(Scalar(Eval(args[2], origin), origin));
                if (col.Kind == FormulaValueKind.Error)
                {
                    return new Item(col);
                }

                column = Math.Truncate(col.Number);
                if (column < 1 || column > Width(range))
                {
                    return new Item(FormulaValue.FromError(column < 1 ? "#VALUE!" : "#REF!"));
                }
            }
            else if (range.FirstRow != range.LastRow && range.FirstColumn != range.LastColumn)
            {
                return new Item(FormulaValue.FromError("#N/A"));
            }

            var modeIndex = name == "VLOOKUP" ? 3 : 2;
            if (args.Count <= modeIndex)
            {
                Fail("UnsupportedFeature", "Approximate lookup is unsupported; specify FALSE or 0.", origin, node.Span, name);
            }

            var mode = Scalar(Eval(args[modeIndex], origin), origin);
            mode = name == "VLOOKUP" ? Logical(mode) : Numeric(mode);
            if (mode.Kind == FormulaValueKind.Error)
            {
                return new Item(mode);
            }

            if (name == "VLOOKUP" ? mode.Boolean : mode.Number != 0)
            {
                Fail("UnsupportedFeature", "Approximate lookup is unsupported.", origin, node.Span, name);
            }

            var search = name == "VLOOKUP" ? new FormulaRange(range.SheetId, range.FirstRow, range.FirstColumn, range.LastRow, range.FirstColumn) : range;
            var wildcard = key.Kind == FormulaValueKind.Text && WildcardMatcher.HasPattern(key.Text) ? new WildcardMatcher(key.Text) : null;
            Action tick = () => Tick(origin, node.Span);
            var index = LookupIndexFor(search);
            if (index != null)
            {
                var found = FindIndexedLookup(index, key, wildcard, tick);
                if (found >= 0)
                {
                    return LookupResult(name, range, found, column, origin, node.Span);
                }

                if (index.Error.HasValue)
                {
                    return new Item(index.Error.Value);
                }

                if (index.Next == Size(search))
                {
                    return new Item(FormulaValue.FromError("#N/A"));
                }
            }

            long next = index?.Next ?? 0;
            var first = Address(search, next);
            var remaining = new FormulaRange(search.SheetId, first.Row, first.Column, search.LastRow, search.LastColumn);
            foreach (var address in Coordinates(remaining, origin, node.Span))
            {
                var position = Position(search, address);
                if (position > next)
                {
                    bulkBlankCells += position - next;
                    RecordLookupValue(index, Number(0), next);
                    if (index != null)
                    {
                        index.Next = position;
                    }

                    if (LookupMatch(key, FormulaValue.Blank, wildcard, tick))
                    {
                        return LookupResult(name, range, next, column, origin, node.Span);
                    }
                }

                var value = ReadCore(address, origin, node.Span);
                if (value.Kind == FormulaValueKind.Error)
                {
                    if (index != null)
                    {
                        index.Error = value;
                    }

                    return new Item(value);
                }

                RecordLookupValue(index, value.Kind == FormulaValueKind.Blank ? Number(0) : value, position);
                if (index != null)
                {
                    index.Next = position + 1;
                }

                if (LookupMatch(key, value, wildcard, tick))
                {
                    return LookupResult(name, range, position, column, origin, node.Span);
                }

                next = position + 1;
            }

            if (next < Size(search))
            {
                bulkBlankCells += Size(search) - next;
                RecordLookupValue(index, Number(0), next);
            }

            if (index != null)
            {
                index.Next = Size(search);
            }

            if (next < Size(search) && LookupMatch(key, FormulaValue.Blank, wildcard, tick))
            {
                return LookupResult(name, range, next, column, origin, node.Span);
            }

            return new Item(FormulaValue.FromError("#N/A"));
        }

        private Item LookupResult(string name, FormulaRange table, long position, double column, FormulaCellAddress origin, SourceSpan span)
        {
            if (name == "MATCH")
            {
                return new Item(Number(position + 1));
            }

            var value = Read(new FormulaCellAddress(table.SheetId, table.FirstRow + (int)position, table.FirstColumn + (int)column - 1), origin, span);
            return new Item(value.Kind == FormulaValueKind.Blank ? Number(0) : value);
        }

        private Item Index(FormulaNode node, FormulaCellAddress origin)
        {
            var args = node.Children;
            var reference = Eval(args[0], origin);
            if (!reference.IsReference)
            {
                if (reference.Value.Kind == FormulaValueKind.Error)
                {
                    return reference;
                }

                Fail("UnsupportedArrayEvaluation", "INDEX requires a single reference area.", origin, node.Span, "INDEX");
            }

            var range = reference.Range;
            if (args.Count == 2 && range.FirstRow != range.LastRow && range.FirstColumn != range.LastColumn)
            {
                Fail("UnsupportedArrayEvaluation", "INDEX of a two-dimensional area requires row and column.", origin, node.Span, "INDEX");
            }

            var row = args[1].Kind == FormulaNodeKind.Missing && range.FirstRow == range.LastRow ? Number(1) : Numeric(Scalar(Eval(args[1], origin), origin));
            var col = args.Count == 3 && !(args[2].Kind == FormulaNodeKind.Missing && range.FirstColumn == range.LastColumn) ? Numeric(Scalar(Eval(args[2], origin), origin)) : Number(1);
            if (row.Kind == FormulaValueKind.Error || col.Kind == FormulaValueKind.Error)
            {
                return new Item(row.Kind == FormulaValueKind.Error ? row : col);
            }

            var r = Math.Truncate(row.Number);
            var c = Math.Truncate(col.Number);
            if (r < 0 || c < 0)
            {
                return new Item(FormulaValue.FromError("#VALUE!"));
            }

            if (r == 0 || c == 0)
            {
                Fail("UnsupportedArrayEvaluation", "INDEX row or column zero returns an unsupported array.", origin, node.Span, "INDEX");
            }

            if (args.Count == 2 && range.FirstRow == range.LastRow)
            {
                c = r;
                r = 1;
            }

            if (r > (long)range.LastRow - range.FirstRow + 1 || c > Width(range))
            {
                return new Item(FormulaValue.FromError("#REF!"));
            }

            int targetRow = range.FirstRow + (int)r - 1;
            int targetColumn = range.FirstColumn + (int)c - 1;
            return new Item(new FormulaRange(range.SheetId, targetRow, targetColumn, targetRow, targetColumn), node.Span);
        }
    }
}
