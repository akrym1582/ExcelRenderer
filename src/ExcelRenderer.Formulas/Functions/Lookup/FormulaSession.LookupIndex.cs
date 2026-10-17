using System;
using System.Collections.Generic;

namespace ExcelRenderer.Formulas
{
    /// <summary>Maintains bounded, incremental lookup indexes without evaluating future rows.</summary>
    public sealed partial class FormulaSession
    {
        private const int MaxLookupIndexedRanges = 16;
        private const int MaxLookupIndexedValues = 65536;
        private const long MaxLookupIndexedTextCharacters = 131072;
        private readonly Dictionary<FormulaRange, LookupIndex> lookupIndexes = new Dictionary<FormulaRange, LookupIndex>(new LookupIndex.RangeComparer());
        private int lookupIndexedValues;
        private long lookupIndexedTextCharacters;

        private static long FindIndexedLookup(LookupIndex index, FormulaValue key, WildcardMatcher? wildcard, Action tick)
        {
            tick();
            if (wildcard == null)
            {
                return index.FirstPositions.TryGetValue(key, out var position) ? position : -1;
            }

            if (wildcard.TryGetLiteral(out var literal))
            {
                return index.FirstPositions.TryGetValue(FormulaValue.FromText(literal), out var position) ? position : -1;
            }

            foreach (var pair in index.TextPositions)
            {
                tick();
                if (wildcard.Matches(pair.Text, tick))
                {
                    return pair.Position;
                }
            }

            return -1;
        }

        private LookupIndex? LookupIndexFor(FormulaRange range)
        {
            if (!lookupIndexes.TryGetValue(range, out var index))
            {
                if (lookupIndexes.Count >= MaxLookupIndexedRanges)
                {
                    return null;
                }

                index = new LookupIndex();
                lookupIndexes.Add(range, index);
            }

            return index.Disabled ? null : index;
        }

        private void RecordLookupValue(LookupIndex? index, FormulaValue value, long position)
        {
            if (index == null || index.Disabled || index.FirstPositions.ContainsKey(value))
            {
                return;
            }

            long textLength = value.Kind == FormulaValueKind.Text ? value.Text.Length : 0;
            if (lookupIndexedValues >= MaxLookupIndexedValues || textLength > MaxLookupIndexedTextCharacters - lookupIndexedTextCharacters)
            {
                index.Disabled = true;
                return;
            }

            index.FirstPositions.Add(value, position);
            if (value.Kind == FormulaValueKind.Text)
            {
                index.TextPositions.Add((position, value.Text));
            }

            lookupIndexedValues++;
            lookupIndexedTextCharacters += textLength;
        }
    }
}
