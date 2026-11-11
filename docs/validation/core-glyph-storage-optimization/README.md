# Core glyph storage follow-up evidence

Baseline: the pre-follow-up working tree from the main rendering implementation,
with source and runtime hashes retained here. The previous main/Core optimization
reports remain historical snapshots. See [implementation](../../core-glyph-storage-optimization.md)
([日本語](../../core-glyph-storage-optimization.ja.md)).

Saved runtimes are under workspace `artifacts/core-glyph-optimization/`:
`before-core`, `after-core`, `before-main`, `after-main`. The main runtimes include
the Core microbenchmark; both stages run the same saved benchmark executable.
Baseline source files are retained separately. Input workbooks are reused from
`docs/validation/core-performance-optimization/inputs` without regeneration.

```sh
source scripts/test-fonts.sh
python3 scripts/measure-core-glyph-storage.py artifacts/core-glyph-optimization
python3 scripts/measure-core-glyph-storage.py artifacts/core-glyph-optimization --controlled
```

Each round alternates stage order. Iteration 0 is cold, iterations 1/2 are further
warm-ups, and iterations 3/4/5 are measured warm samples. Whole conversions use
the existing Core validation harness; microbatches use unique >2048-character
inputs and exclude input glyph lookup/font loading/digest work from timing and
allocation. Exact output digests include metrics, glyphs, source bytes, clusters
and advances. Peak RSS is process-wide and includes the excluded validation work.
Primary and tiering-disabled protocols are recorded separately. Inclusive phase
times are not added. Per-round time/RSS flags are retained rather than replaced
with a pooled-median pass.

## Executed results

- Debug/Release solution Rebuild: 0 warnings / 0 errors.
- Whole solution: 3073 passed / 0 failed / 0 skipped; new strict/replay group: 25 passed.
- Core coverage: 455 tests; line 84.82% / branch 74.34%; Performance: 8 passed.
- Seven saved Core PDF pairs match in pixels, text/glyph coordinates, drawings, images, links and embedded subset bytes.
- Main PDF/PNG/SVG: 24 saved output files match. Existing 25 PDF fixtures have zero unmet gates; frozen B has zero differences.
- Main/Core parity: 19 direct comparisons at 144/288 dpi. Reader/Core isolation and dependency checks passed.
- Public API remains main 2361 / Core 82 entries, unchanged. Windows was not executed.

## Per-round measurements

These ratios are after/before for this follow-up only. Allocation summaries below are per-round medians; no pooled-median acceptance is used. Micro workloads use 20 unique inputs per batch, and exact output hashes match in every trial.

| Case | Primary time ratios (rounds 0/1/2) | Allocation ratios | Controlled time ratios |
| --- | --- | --- | --- |
| micro-clusters | 0.553 / 1.683 / 0.622 | 0.407 / 0.407 / 0.407 | 0.486 / 0.490 / 0.733 |
| micro-compatibility | 1.074 / 1.227 / 1.227 | 1.000 / 1.000 / 1.000 | 1.026 / 1.003 / 1.003 |
| micro-outline-80 | 0.862 / 0.743 / 0.793 | 0.576 / 0.576 / 0.576 | 0.819 / 1.010 / 1.087 |
| micro-outline-600 | 0.747 / 0.669 / 0.688 | 0.455 / 0.454 / 0.454 | 0.576 / 0.547 / 0.535 |
| micro-outline-100000 | 0.725 / 0.727 / 0.787 | 0.431 / 0.431 / 0.431 | 0.656 / 0.667 / 0.625 |
| text1 | 3.916 / 0.596 / 0.970 | 1.000 / 1.001 / 1.000 | 0.986 / 0.854 / 0.866 |
| text1000 | 0.984 / 1.022 / 0.868 | 0.954 / 0.954 / 0.954 | 0.749 / 1.024 / 1.053 |
| wrap1000 | 0.938 / 0.855 / 1.038 | 0.634 / 0.633 / 0.633 | 0.789 / 0.772 / 0.755 |
| borders1000 | 0.962 / 0.850 / 1.355 | 0.959 / 0.959 / 0.955 | 0.760 / 1.148 / 0.931 |
| empty | 0.789 / 0.955 / 0.987 | 1.001 / 1.000 / 0.999 | 1.227 / 0.425 / 0.973 |
| imagesonly | 0.740 / 1.217 / 1.166 | 1.000 / 0.999 / 1.001 | 0.790 / 0.988 / 1.026 |
| header | 2.095 / 0.942 / 0.446 | 0.999 / 1.000 / 1.001 | 1.113 / 0.766 / 0.387 |

Identity-cluster construction allocation is 5.604 → 2.283 MiB per microbatch
(about 59% less). Outline width 80 allocation is 12.115 → 6.983 MiB (about 42%
less); wider outline microcases reduce allocations by about 55–57%. The complete
Core wrap1000 workbook is approximately 213.6 → 135.3 MiB (about 36.6% less) in
the primary protocol. Controlled wrap1000 time ratios are 0.789 / 0.772 / 0.755;
these settings are separate and do not replace primary results.

The all-case 5% time/RSS non-regression condition is **not established**. Primary
micro-cluster/compatibility timings and small whole-conversion cases vary; even
controlled runs retain time/RSS flags in some rounds. The different-source
compatibility path has unchanged allocation and controlled time ratios
1.026 / 1.003 / 1.003, while primary ratios are 1.074 / 1.227 / 1.227. This is
retained rather than claimed as a pass. All individual cold/warm observations,
GC counts and process RSS peaks remain available. Neither micro timings nor
previous main/Core performance percentages are general Core improvement rates.

## Ownership and validation details

Wrapped outline lines previously materialized a range array and then cloned it
for GlyphSequence. Core now performs only the final storage copy. Unwrapped lines
already had a single copy and keep that count. Identical source/display cluster
construction now has one element enumeration rather than two, and directly owns
its cluster array. Distinct source/display validation and explicit zero clusters
keep their prior behavior. Metrics `glyphStorageCopies`, `glyphStorageCopiedGlyphs`
and `glyphClusterElementPasses` are recorded separately from primary timing;
`outlineIntermediateGlyphCopies` is absent on the Core range path. Read-only
storage is verified against mutation of the borrowed input array. Existing public
main layout replay tests and a new disposed-scope Core replay test pass.

## Reproduction and retained state

`core-only.patch` applies only the three Core source changes to the pre-follow-up
working tree. `follow-up.patch` also adds the strict tests and shared benchmark
harness. First reproduce the prior main implementation using its retained product
patch when starting from the bare base commit. `before-sources/` and
`source-state.json` make this dependency explicit. Both stages use the same saved
benchmark executable, with original/final product DLLs beside it. Runtimes remain
under workspace artifacts; all input/font/DLL hashes are retained here.

The first controlled attempt had PDF raster validation overlapping its opening
samples. It was excluded in full and remains in workspace artifacts. The retained
controlled run was rerun without overlapping builds, tests or rendering. Existing
historical main optimization evidence was not rewritten.

Four distribution packages were packed locally and audited; no packages were published. Whole-conversion diagnostics record 76000 final glyph storage copies, 1117780 copied glyphs, and 76000 element passes for wrap1000, with no intermediate range copies.
