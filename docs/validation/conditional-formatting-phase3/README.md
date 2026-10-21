# Conditional formatting Phase 3 validation

The original Phase 3 evidence is retained below. See [review fixes](#review-fixes-icon-text-space-and-omitted-limit-diagnostics) for the subsequent corrections and current verification.

Start: `021d1f921573ea8976ded785162693f17ea06883` (latest fetched origin/main on 2026-10-21).
Branch: `feature/conditional-formatting-phase3`.
Implementation end: `c8697adce9b0cf160b4aa36cbe8cf96aacc4188c`. The final documentation/evidence commit follows this revision.
Runtime DLL/input SHA-256 values are retained with each benchmark; binaries were built from this code before committing it.

This validates the fixed Phase 3 product profile. **Phase3プロファイル検証済み／Excel比較未実施**.
No desktop Excel process was available. The negative-bar interpolation, equal-value
rules, palette, icon vectors and layout constants are product decisions, not measured
Excel compatibility claims. Gradients intentionally render as solid fills with warnings.

## Reproduction

```sh
python3 scripts/generate-conditional-formatting-phase3.py
dotnet restore ExcelRenderer.slnx
dotnet build ExcelRenderer.slnx -c Release -t:Rebuild --no-restore --verbosity:minimal -m:1
source scripts/test-fonts.sh
EXCELRENDERER_CF_PHASE3_ARTIFACTS="$PWD/artifacts/conditional-formatting-phase3" dotnet test ExcelRenderer.slnx -c Release --no-build --no-restore -m:1
dotnet test tests/ExcelRenderer.Core.Tests/ExcelRenderer.Core.Tests.csproj -c Release --no-build --filter "Category!=Performance" -p:CollectCoverage=true
dotnet test tests/ExcelRenderer.Core.Tests/ExcelRenderer.Core.Tests.csproj -c Release --no-build --filter "Category=Performance"
python3 scripts/check-source-namespaces.py
python3 scripts/check-core-dependencies.py
python3 scripts/check-no-pdfsharp.py
python3 scripts/validate-reader-isolation.py
python3 scripts/validate-conditional-formatting-phase3.py --artifacts artifacts/conditional-formatting-phase3 --baseline /workspace/ExcelRenderer-phase3-baseline
```

Linux tests use the committed isolated Fontconfig setup. Build, test and benchmark
processes run sequentially. Test execution needs the test runner's local communication
socket; the restricted shell denied it, so tests used the environment's reviewed
execution permission. NuGet restore likewise used the normal configured proxy.

## Retained fixtures

The Python standard-library generator creates [legacy-bars.xlsx](fixtures/legacy-bars.xlsx),
[x14-bars.xlsx](fixtures/x14-bars.xlsx), [icons.xlsx](fixtures/icons.xlsx) and
[layout-mapping.xlsx](fixtures/layout-mapping.xlsx). The last fixture is an expandable
Mapping template. Tests save its expanded form under `artifacts/conditional-formatting-phase3/`.
Other retained fixtures are rendered as-is to PDF, PNG, SVG and PostScript, and to
continuous PNG/SVG. Tests retain all five icon sets with low/middle/high values.

The fixed interval assertions use hand-calculated values from the supplied plan,
with 1e-6 tolerance. Icon boundary tests explicitly cover inclusive/exclusive/equal
thresholds, reverse, percent versus percentile, typed numeric populations, linked
GUID normalization, RTL, malformed attributes, priority composition, rollback,
rich-text preservation and public-layout/continuous command geometry. PNG tests check
actual palette pixels; PDF and PostScript also undergo independent Ghostscript
parsing/rasterization. The shared command path supplies SVG polygon rendering.

## Performance protocol

Use a detached worktree at the start SHA to build and retain the baseline Tool Release
runtime; do not build over a runtime during measurement. The benchmark accepts saved
runtime directories and saves input/assembly hashes, all warmups/samples, per-round
medians and numeric population counters:

```sh
python3 scripts/benchmark-conditional-formatting-phase3.py \
  --before /workspace/ExcelRenderer-phase3-baseline/src/ExcelRenderer.Tool/bin/Release/net10.0 \
  --after src/ExcelRenderer.Tool/bin/Release/net10.0 \
  --output artifacts/conditional-formatting-phase3-benchmark
```

Each case has 10,000 cells; each separate process runs 3 warmups and 10 measurements.
Three rounds alternate before/after execution order. No-CF, disabled-CF and Phase 2
color-scale cases compare both runtimes. Bars, icons and a shared bar+icon population
run on the new runtime. A follow-up can pass `--fixture-source` to reuse identical XLSX
bytes. Sorting is not needed for min/max bars or numeric icon thresholds; percentile
sharing is separately asserted by a 10,000-cell test.

## Optional Excel reference capture on Windows

Use the existing [capture script](../../../scripts/capture-conditional-formatting-excel.ps1)
with desktop Excel installed. For example:

```powershell
./scripts/capture-conditional-formatting-excel.ps1 -InputWorkbook ./docs/validation/conditional-formatting-phase3/fixtures/x14-bars.xlsx -OutputDirectory ./artifacts/excel-phase3-reference
```

Repeat for legacy-bars.xlsx and icons.xlsx. Capture the expanded layout-mapping.xlsx
from the test artifacts directory, not the unexpanded template. Preserve Excel/version,
font and printer details with the reference PDFs. Reference capture is optional and
was not used to establish the fixed profile's expected intervals or icons.

## Results

- Release rebuild: zero warnings, zero errors.
- Solution tests: **2,817 passed**, none failed or skipped (Core 403, Formulas 833,
  Mapping 172, OpenXml 213, main 1,078, Tool 118).
- Core CI gate: **82.78% lines / 71.27% branches**; 395 non-performance cases and
  all 8 separate performance cases passed.
- Standalone Reader/Core restore, publish, dependency graphs, AssemblyRef inspection
  and separate-process execution passed. Namespace, ownership, removed-dependency and
  package-publication allowlist checks passed.
- Public API: main and Core unchanged; Reader has 11 additive public contract entries,
  with no removals or changes to existing signatures/nullability.
- Ghostscript parsed and rasterized all **18** retained PDF/PostScript files;
  pdfinfo parsed the PDFs. PNG tests also assert actual icon palette pixels.
- Fixtures now place worksheet extLst last, as required by worksheet element ordering.

Logs are under [results](results/). Full visual outputs, including the expanded
Mapping workbook, are under [outputs](outputs/). API inventories are under [api](api/).
Examples: [signed bars](outputs/x14-bars-continuous.png),
[all icon sets](outputs/icons-continuous.png), [PDF](outputs/icons.pdf),
[PostScript](outputs/icons.ps), [SVG](outputs/icons.svg).

### Recorded performance results

All raw runs are retained, including unsuccessful/noisy attempts:

1. `performance/initial`: default tiered compilation, 3 rounds; substantial between-process
   variation, so these timings do not establish the 5% goal.
2. `performance/tiered-off`: equal `DOTNET_TieredCompilation=0`, 3 rounds.
3. `performance/disabled-followup`: same bytes, disabled-CF only, 5 rounds.
4. `performance/allocation-cleanup`: redundant intermediate rule arrays removed;
   tiering disabled and both runtimes pinned equally to one allowed CPU, 5 rounds.
5. `performance/complete`: final namespace-boundary checks; same controlled JIT/CPU
   settings, same fixture bytes, 3 rounds. This is the final DLL comparison below.

The allocation target is judged separately from time. The time target is **not claimed
from a pooled median**. Each round's before/after medians and goal flags are retained
in [performance/goals.json](performance/goals.json). Where a round exceeds 5%, timing
remains unconfirmed rather than being reported as passing. No unconditional claim of
5% latency equivalence under default runtime settings is made.

The final controlled run used `--stable-jit --pin-cpu auto --rounds 3 --fixture-source
artifacts/conditional-formatting-phase3-benchmark`; its actual CPU affinity is recorded
in `performance/complete/summary.json`. Every process still has 3 warmups and 10 measured
reads. No build, test or render process overlapped these measurements.

| Case | Round | Before ms | After ms | Time change | Allocation change | Populations / sorts |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| no-cf | 1 | 284.76 | 225.10 | -20.95% | +0.13% | 0 / 0 |
| no-cf | 2 | 179.62 | 188.06 | +4.70% | +0.12% | 0 / 0 |
| no-cf | 3 | 161.99 | 191.25 | +18.07% | +0.12% | 0 / 0 |
| disabled | 1 | 219.32 | 235.60 | +7.42% | +0.13% | 0 / 0 |
| disabled | 2 | 166.75 | 164.93 | -1.09% | +0.12% | 0 / 0 |
| disabled | 3 | 192.46 | 187.76 | -2.44% | +0.12% | 0 / 0 |
| phase2 | 1 | 275.93 | 266.35 | -3.47% | +0.09% | 1 / 0 |
| phase2 | 2 | 263.11 | 264.64 | +0.58% | +0.09% | 1 / 0 |
| phase2 | 3 | 226.45 | 235.91 | +4.18% | +0.09% | 1 / 0 |
| bars | 1 | — | 260.06 | — | — | 1 / 0 |
| bars | 2 | — | 284.21 | — | — | 1 / 0 |
| bars | 3 | — | 226.96 | — | — | 1 / 0 |
| icons | 1 | — | 264.27 | — | — | 1 / 0 |
| icons | 2 | — | 254.78 | — | — | 1 / 0 |
| icons | 3 | — | 227.79 | — | — | 1 / 0 |
| shared | 1 | — | 247.90 | — | — | 1 / 0 |
| shared | 2 | — | 291.24 | — | — | 1 / 0 |
| shared | 3 | — | 234.99 | — | — | 1 / 0 |

For the new 10k bar, icon and shared-population cases, every measured read retained one population, read 10,000 population values and performed zero numeric sorts. The separate percentile test confirms one shared sort rather than a sort per rule or page.

## Publication

The user explicitly authorized publishing the complete changes, including API
inventories, rendering samples and performance evidence, to the public repository
`akrym1582/ExcelRenderer` as a draft pull request.
Implementation revision IDs above identify the local development history. The
published snapshot may use a single commit with identical Git tree contents.

## Review fixes: icon text space and omitted-limit diagnostics

These fixes start from fetched `origin/main`
`e0b9e22920eb0f7ba6dc7084fddc44e96d80227d`. They correct the original
Phase 3 icon reservation policy; the earlier 8pt-content/8pt-reservation
expectation was unsafe for visible shrink-to-fit values.

All icon decisions use source points before page scaling and full cell geometry
before clipping. For ordinary content width W, visible values retain
`M = max(1pt, original font size)` and the candidate icon width is
`min(12, max(0, W - 2 - M))`. Icon-only cells retain the previous candidate width
`min(12, max(0, W - 2))`. Widths below 3pt or full cell heights at or below 2pt
omit the icon, reserve no space and restore ordinary text layout and visibility.
Otherwise the icon reserves its width plus 2pt on the left, or on the right for
RTL. This minimum preserves text space, without guaranteeing that every complete
value fits. This is a product display policy, not a claim of Excel equivalence.

The source row geometry is final before text measurement in both paginated and
continuous pipelines. Measurement therefore uses the final full cell height on
its first pass; no retry, inverse shrink operation, source cell mutation or
page-clip-dependent measurement is needed. Core exposes only the generic internal
height argument for product insets. Core does not interpret conditional rules.

Limit fallback now searches affected rule rectangles intersected with finite
output regions, including merged anchors, through the existing exact calculation
target predicate. Source row/column, selection, print-title, merge and selected-page
boundaries divide that predicate into constant bands. One representative per band
skips coordinate areas with identical visibility. Every candidate/rule/border/merge
budget failure uses the same whole-sheet fallback search, rather than an incomplete
candidate collection. Diagnostics keep the existing Warning/Read code, aggregation,
selection filtering and failure-policy behavior.

Target search distinguishes found, absent and indeterminate. Band/rectangle work
is bounded, cancellation remains cooperative, and an arbitrary internal predicate
without declared geometry boundaries retains a bounded coordinate search of at
most two million steps. An indeterminate search fails explicitly instead of
silently treating it as absence or attaching a warning to unrelated output.
The search index is allocated only on the exceptional limit path.

The [review samples](review-fixes/) retain a new 8pt ordinary-content workbook
(with explicit 1px Normal-font MDW), PNG/SVG outputs in both image layouts, and the
[pre-fix failures](review-fixes/before.log). Tests assert the actual 9pt column
width and 8pt content width. PNG/SVG raster checks find dark digit ink; separate
text-only PNG checks exclude borders and icons. Low-height cases compare ordinary
and decorated measurements and content rectangles rather than requiring pixels.
No frozen reference data was regenerated, and no comparison with desktop Excel
was performed for these fixes.

The merged-output regression additionally reproduced a downstream filter loss:
an A1 representative on a hidden row, or outside an explicit selection, was
filtered using the anchor's single coordinate even when its merged cell was
visible. Read-diagnostic filtering now tests the complete source cell span and
its intersection with the selected output. Both full and clipped merged-output
cases retain the A1 warning; unrelated ranges and sheets remain excluded. The
[filter pre-fix log](review-fixes/merged-filter-before.log) records both failures.

Current local verification: Release rebuild **0 warnings / 0 errors**, all **2,853**
solution tests passed (including 1,114 main-library tests); the strengthened
ordinary-layout/glyph comparisons also passed all 6 cases. Core correctness
coverage passed 395 cases at **82.80% lines / 71.31% branches**, followed by all
8 performance cases in a separate run. All 19 output mutations, both shared
geometry mutations and all 4 Core mutations were detected and sources restored.
The three public API/nullability inventories are unchanged. Independent checks
validated 17 Core raster pages and 18 PDF/PostScript files. Both retained PNG
layouts contain 30 dark digit pixels.

[Validation details and changed-file inventory](review-fixes/validation.json)
link the exact commands, pre/post results, limits, remaining platform checks and
retained logs. Desktop Excel comparison and local Windows execution remain
unperformed because this environment is Linux without desktop Excel; the existing
PR CI includes Windows Core tests.


### CI follow-up: dependency audit

PR #107's initial CI run (39590171832) passed compilation and tests, including
Windows Core coverage/performance, but its three distribution audit jobs rejected
the audit command's own filename in the new machine-readable command inventory.
The inventory is scanned as configuration, so the command transcript belongs here
alongside the existing Markdown reproduction instructions:

```sh
python3 scripts/check-no-pdfsharp.py
python3 scripts/check-no-pdfsharp.py --packages artifacts/pr107-ci-packages --publish artifacts/pr107-ci-publish
python3 scripts/check-no-pdfsharp-mutations.py
```

The dependency audit and its allowlist remain unchanged. This is a documentation
correction; it adds no dependency, production-code change or audit exemption.

Local reproduction failed on the same inventory entry before this correction.
After moving that record, the audit passed over all 24 restored projects and all
four packed distributions plus the Tool publish output (817 managed assembly
inventories). The existing contamination checks still rejected a source reference,
a restored transitive reference, an AssemblyRef hidden behind an innocent filename
and a packed dependency; their final restored audit passed. This follow-up changes
only the command inventory, this report and retained-artifact hashes.
