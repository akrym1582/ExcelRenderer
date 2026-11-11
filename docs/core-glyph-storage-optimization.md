# Core glyph storage and cluster construction

This follow-up implements two changes requested after the main-rendering work.
Its baseline is the saved **pre-follow-up working tree**, which already includes
that earlier implementation; it is not the older PR #114 baseline. Retained
source/DLL hashes distinguish the two comparisons. [日本語](core-glyph-storage-optimization.ja.md).

## Implementation

- `OutlineParagraph.BreakRanges` enumerates line text and paragraph-relative glyph
  offsets/counts. Core's measurer asks the paragraph to create a `GlyphSequence`
  directly from the borrowed range. Its span constructor copies once into the
  selection's private array. No line glyph array is materialized first; completed
  lines retain neither the paragraph buffer nor its native font owner.
- `OutlineParagraph.Break` remains an adapter for existing main-library callers.
  Its no-wrap behavior and array ownership are preserved. The range/wrap algorithm
  is shared rather than duplicated into another product.
- `GlyphSequence` retains the array constructor and adds an internal borrowed-span
  constructor. Both freeze input into privately owned storage. Advances are
  calculated from the frozen array. Read-only array views remain unchanged.
- When source and display are ordinally equal, cluster construction enumerates
  Unicode text elements once, writes the final cluster array directly, preserves
  consecutive whitespace grouping and computes the same per-element UTF-8 byte
  offsets. Surrogate/scalar counting and glyph-count errors remain checked.
- Different source/display text retains the previous two-enumerator algorithm,
  including mismatched/unused-source validation. Explicit single-cluster glyphs
  still receive zero offsets. No font-selection, line-break, arithmetic-order,
  public API, TFM, dependency, reader or output-format policy is added.

The range-copy reduction is used by Core's ordinary outline layout. The cluster
primitive is Core-owned and shared; main's equivalent selections also use its
identity path. Main keeps its existing wrapping/fallback adapters.

## Validation

`GlyphStorageOptimizationTests` contains an independent snapshot of the old
cluster algorithm. Fixed-seed cases include combining sequences, surrogate pairs,
ZWJ emoji, regional indicators, whitespace, invalid UTF-16, mismatched glyph
counts, differing source/display and explicit clusters. Error types/messages,
source bytes, glyphs, clusters and advances are compared exactly. A borrowed-slice
mutation test checks immutable storage; a scope-disposal test collects/prepares/
paints a saved Core layout with a new font owner. Existing tests compare complete
wrap geometry at adjacent representable widths and replay public main layouts
after disposal/source deletion.

Builds, tests and saved-runtime measurements are sequential. Micro measurements
exclude font loading, glyph lookup inputs and digest computation from allocation/
time; results include an exact layout/glyph/source/cluster/advance digest. Unique
inputs exceed 2048 UTF-16 units, preventing layout-cache admission. Whole Core
conversions reuse committed fixture bytes and the existing validation harness.
Process peak RSS includes validation/digest work and is not reset per iteration.

See [retained validation and raw samples](validation/core-glyph-storage-optimization/README.md).
Windows execution and an all-case 5% time/RSS non-regression guarantee must be
reported separately from Linux correctness and allocation results.

## Results and limits

Debug/Release Rebuild completed with zero warnings/errors; all 3073 solution
tests passed. Core coverage is line 84.82% / branch 74.34%, and eight Performance
tests passed. Public API snapshots are unchanged. Saved Core PDF pairs, main
PDF/PNG/SVG outputs, fixed PDF comparisons, parity and isolation checks passed.

In the saved Core wrap1000 workload, allocation decreased from approximately
213.6 to 135.3 MiB (about 36.6%). Cache-miss outline microbatches reduce allocation
by about 42–57%, and identical-source cluster microbatches by about 59%.
Timing/RSS still has flagged rounds; no all-case 5% non-regression pass or Windows
execution is claimed. Full per-round values are retained separately from the
previous main-rendering results.
