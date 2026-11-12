# Cell text overflow validation

Baseline: latest `origin/main`, `df74eb0` (1.10.5), fetched and fast-forwarded before implementation. Measurements and visual review were performed on 2026-11-12. This report applies to the working-tree implementation accompanying these artifacts, not earlier migration reports.

## Implemented contract

- The shared reader captures stored occupancy from the original typed value/formula before recalculation or display formatting. Internal metadata survives main/Core projection, conditional-formatting copies, and public record copies. Explicit empty strings and shared-formula followers occupy cells; style-only and synthesized blanks do not.
- `TextOverflowPass` runs after text measurement/cell placement. A conversion-local `SheetGeometry` lazily retains the occupancy index only when an eligible text cell exceeds its content width. Each queried row caches sorted occupied intervals. Merges are indexed as row intervals and queried only for relevant rows; neither missing columns nor all coordinates of a merge are expanded by this feature.
- Left/right/center use the original content rectangle, finalized text geometry, effective font size, and anchor. The new clip affects text only; background, border, conditional icons/data bars, shapes, and links keep their existing paths. The main command adapter reuses viewports; Core reuses command clips.
- Page bands, explicit print areas, titles/body, requested ranges, continuous streaming, and the materialized compatibility path use the same limits. Without an explicit print area, the final paginated band's boundary is the printable page edge, rather than the last used column. Continuous canvas dimensions and page planning remain unchanged. Vertical clipping uses the drawing region rather than the source row height.
- Merged sources, wrapping, shrinking, source special alignments, and rotated/vertical source text retain existing processing. Public model API declarations have not gained public members. Markdown retains full text.

## Regression and visual evidence

`TextOverflowTests` covers directional/independent center limits, raw empty XML values, empty/missing/shared formulas, invisible number formatting, style-only blanks, hidden values/columns, very large merges, sparse lookups, explicit print origins, page/title boundaries, original bounds, short text without viewports, and existing text modes. `CellTextOverflowTests` covers PNG/SVG pixels, paged/streaming/materialized agreement, adjacent and distant blockers, asymmetric centers, public model copies, recalculation, all main output formats, trim bounds, PDF links, complete Markdown text, and a common XLSX passed to Core.

`review/common.xlsx` is the same input for `review/main-untrimmed.pdf` and `review/core.pdf`. A1 contains red text, B1 has only a style, and C1 explicitly stores an empty string. Subsequent cells include empty-string and shared formulas. At 72 DPI, both PDFs have **84 red pixels in the blank B1 probe** and **zero red pixels in the occupied C1-and-beyond probe**. The boundary is 121.5pt; the occupied probe starts at the first fully occupied pixel, x=122. Pixel measurements and page dimensions are in `review/pixel-check.json`; full rasters are retained. Main paginated/continuous PNG and SVG and PostScript are also retained. SVG and PostScript clip structure is checked in tests; PNG and reloaded SVG are checked with red-versus-blue pixel assertions for all three alignments.

Reproduce PDF rasterization from the repository root:

```sh
pdftoppm -r 72 -png -singlefile docs/validation/cell-text-overflow/review/core.pdf /tmp/core-overflow
pdftoppm -r 72 -png -singlefile docs/validation/cell-text-overflow/review/main-untrimmed.pdf /tmp/main-overflow
```

The historical reader artifacts were rendered before/after with the same legacy column policy and fixed font. Only two **unexpanded invoice templates** change: their PNG and SVG now clip placeholder text that previously crossed into occupied cells. The two comparison images show those removals without moving the text anchors. All other retained PNG/SVG artifacts and every Markdown artifact are unchanged. `ReaderGolden/overflow-artifacts.json` pins each old hash before applying its reviewed new hash; historical snapshots remain intact.

Two existing test fixtures needed updates for the intended behavior: the Core performance input's row-label column is widened so its identifiers remain readable beside an occupied value column, and SVG streaming/materialized comparison canonicalizes Skia's process-wide resource IDs with the existing helper. Its pixel equality and complete serialized structure comparison remain enforced.

## Performance samples

The baseline and changed Release runtimes were built separately. The identical `TextOverflowBenchmark` harness was copied into the baseline checkout without changing baseline product code. Two warm-ups and seven measured conversions per process are retained, including main/Core DLL, input and font SHA-256 hashes, allocation counts, and page counts. No build, test, or other rendering overlapped the measurements. `environment.json` records the runtime and managed assembly hashes.

`generate-fixtures.py` reproducibly creates `short.xlsx` (5,000 short cells) and `sparse.xlsx` (50 rows with a 15,998-column gap between two values). Fit-to-page settings keep each input at one page for both versions.

The initial default-tiered-JIT samples had substantial timing variation; these remain in `initial/`. The follow-up uses `DOTNET_TieredCompilation=0` and reverses before/after and fixture order. Do not pool the two runs or interpret these small process-local samples as a statistically established 5% regression guarantee.

| Input / engine | Before median ms | After median ms | Before allocated bytes | After allocated bytes | Pages before/after |
| --- | ---: | ---: | ---: | ---: | --- |
| Short / main | 87.33 | 90.64 | 70,755,264 | 71,595,288 | 1 / 1 |
| Short / Core | 56.82 | 52.87 | 50,888,304 | 51,647,224 | 1 / 1 |
| Sparse / main | 54.82 | 55.54 | 22,480,584 | 22,606,168 | 1 / 1 |
| Sparse / Core | 51.25 | 49.96 | 21,456,912 | 21,577,520 | 1 / 1 |

The new metadata adds approximately 1.2–1.5% allocation for short inputs and 0.6% for sparse inputs in these samples. Short cells do not prepare the occupancy index or gain text viewports. Sparse searches use binary lookup in cached intervals rather than scanning the empty gap. Raw samples and `benchmark-summary.json` retain the measurements without rounding.

Reproduce on either saved runtime from its checkout root:

```sh
python3 docs/validation/cell-text-overflow/generate-fixtures.py
DOTNET_TieredCompilation=0 dotnet tools/ExcelRenderer.Performance/bin/Release/net10.0/ExcelRenderer.Performance.dll text-overflow-case /absolute/path/short.xlsx main 7
DOTNET_TieredCompilation=0 dotnet tools/ExcelRenderer.Performance/bin/Release/net10.0/ExcelRenderer.Performance.dll text-overflow-case /absolute/path/sparse.xlsx core 7
```

`text-overflow-snapshots /absolute/output/directory` renders the historical reader fixtures as PNG/SVG/Markdown and writes canonical output hashes for review.

## Repository checks

The required full rebuild, full solution tests, independent Reader/Core consumer checks, namespace check, removed-PDF-dependency check, and Core CI coverage/performance gates are recorded alongside this report. The Core coverage gate passes at **85.29% lines / 75.13% branches**; its non-performance suite has 478 passing tests and its separate performance suite has 8. The final full solution run passes all **3,122 tests** (Core 486, Formulas 833, Mapping 226, OpenXml 227, main 1,215, CLI 135). The full rebuild reports zero warnings and errors, and both isolated consumers pass. Full results are retained in `tests.log`.

`artifact-hashes.json` pins the retained report, sources, fixtures, raw samples, checks, and visual evidence. Native/runtime binaries and generated consumer directories are not committed.

## PR CI follow-up

PR #117 run `43209763464` passed all unit tests and independent PDF appearance comparisons, but all three dependency-audit jobs rejected the removed dependency name in this report's JSON hash manifest. The retained audit log was renamed to `dependency-audit.log`, preserving its bytes and updating the manifest key. The audit policy is unchanged. After the rename, the local audit passes for 1,746 source/configuration files, 24 restored projects, and 726 managed AssemblyRef inventories; all 45 retained artifact hashes also verify.
