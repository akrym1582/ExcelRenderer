namespace ExcelRenderer.Formulas.Evaluation
{
    /// <summary>Contains session-local work counters; missing blank intervals require no cell reads.</summary>
    public readonly struct FormulaSessionStatistics
    {
        /// <summary>Initializes a new instance of the <see cref="FormulaSessionStatistics"/> struct.</summary>
        /// <param name="reads">Source calls.</param>
        /// <param name="candidates">Cursor candidates.</param>
        /// <param name="blanks">Bulk blank positions.</param>
        /// <param name="cache">Retained values.</param>
        /// <param name="visits">Inspected cell coordinates.</param>
        /// <param name="steps">Budgeted steps.</param>
        /// <param name="lookupValues">Typed lookup entries.</param>
        /// <param name="lookupRanges">Indexed lookup vectors.</param>
        /// <param name="indexWork">Built-in coordinate index operations.</param>
        internal FormulaSessionStatistics(long reads, long candidates, long blanks, int cache, int visits, int steps, int lookupValues, int lookupRanges, long indexWork)
        {
            IndexWork = indexWork;
            SourceReads = reads;
            RangeCandidates = candidates;
            BulkBlankCells = blanks;
            CachedValues = cache;
            CellVisits = visits;
            EvaluationSteps = steps;
            LookupIndexedValues = lookupValues;
            LookupIndexedRanges = lookupRanges;
        }

        /// <summary>Gets built-in index dictionary probes, binary-search comparisons and inspected rows or coordinates, including dense coverage checks.</summary>
        public long IndexWork { get; }

        /// <summary>Gets retained typed lookup entries, limited to 65536 per session.</summary>
        public int LookupIndexedValues { get; }

        /// <summary>Gets indexed lookup vectors, limited to 16 per session.</summary>
        public int LookupIndexedRanges { get; }

        /// <summary>Gets actual source calls, excluding cache hits.</summary>
        public long SourceReads { get; }

        /// <summary>Gets coordinates advanced by range cursors.</summary>
        public long RangeCandidates { get; }

        /// <summary>Gets missing blank positions processed in bulk.</summary>
        public long BulkBlankCells { get; }

        /// <summary>Gets retained nonblank values.</summary>
        public int CachedValues { get; }

        /// <summary>Gets evaluated or inspected cell coordinates, including cache hits.</summary>
        public int CellVisits { get; }

        /// <summary>Gets budgeted validation, cursor and evaluation steps.</summary>
        public int EvaluationSteps { get; }
    }
}
