# Conditional formatting Phase 2 validation

Fetched base: `origin/main` = `9ac854f97bfeebc511f89f36e6f340376cfb35d8`, matching
the task's 2026-10-19 plan. The initial `work` checkout was clean. Work branch:
`feature/conditional-formatting-phase2`; implementation commit:
`ab4ad115f700e05e4aae659e99910326568ec442`. The preceding implementation is `93079af0193d8cec498d690a7615c89057e7a5d0`.
Follow-up evidence commits add retained artifacts. This report records local validation;
version bumps and package publication are outside its scope.

Linux x64, .NET SDK 10.0.401, runtime 10.0.12. All results below describe this
environment. Windows/desktop Excel behavior is not established.

## Implementation and boundaries

Main supports duplicate/unique values, top/bottom N or N%, above/below/inclusive
mean and population-standard-deviation rules, and standard two/three-color scales.
Reader resolves descendant scale colors and bounds cfvo val lengths. OpenXml exposes
sparse value coordinates on the existing calculation snapshot. Main owns rectangle
unions, typed frequency keys, numeric statistics, thresholds and dynamic fill priority.
The existing Mapping population/formula-store rewriter is reused without a second
rewriting algorithm. All five types survive expansion, save/reload and PDF/PNG/SVG
rendering in the main tests; absolute cfvo val references move to expanded rows.
Existing Mapping tests retain explicit failures for application sets that cannot
preserve formula references, nested/noncontiguous cases, and unsupported-rule metadata.

Core source is unchanged: it references only Reader and renders ordinary saved styles.
No new project, package, paid dependency, function engine extension, dedicated switch
or CLI flag was added. Data bars, icon sets, timePeriod, x14 rendering and conditional
number formats remain unsupported. Frozen historical PDFs and hashes were not changed.

See [the complete contract](../../conditional-formatting.md), including the typed
comparison table and all named budgets. Missing caches and evaluation failures
invalidate the entire affected population. Implicit blanks use checked long area;
no missing-coordinate list or full-sheet cell HashSet is allocated. Dynamic colors
use a typed fill, not a generated XML differential per cell.

## Reproduction

From the repository root:

```bash
dotnet restore ExcelRenderer.slnx
dotnet build ExcelRenderer.slnx -t:Rebuild --no-restore --verbosity:minimal -m:1
dotnet build ExcelRenderer.slnx -c Release -t:Rebuild --no-restore --verbosity:minimal -m:1
source scripts/test-fonts.sh
EXCELRENDERER_CF_PHASE2_ARTIFACTS="$PWD/artifacts/conditional-formatting-phase2" dotnet test ExcelRenderer.slnx --no-restore -m:1
python3 scripts/check-source-namespaces.py
python3 scripts/check-core-dependencies.py
python3 scripts/check-formulas-dependencies.py
python3 scripts/check-no-pdfsharp.py
python3 scripts/validate-reader-isolation.py
dotnet pack ExcelRenderer.slnx -c Release --no-build --no-restore -p:BuildProjectReferences=false -o artifacts/packages-conditional-formatting-phase2-final
python3 scripts/package_distribution.py --packages artifacts/packages-conditional-formatting-phase2-final --output artifacts/phase2-package-inventory-final.json
python3 scripts/validate-formulas-packages.py --packages artifacts/packages-conditional-formatting-phase2-final --work /tmp/excelrenderer-phase2-final-consumers
```

Rebuild and tests must run sequentially against a given configuration. One earlier
concurrent attempt rebuilt the Tool output while its integration tests were running;
that FileNotFoundException run is not acceptance evidence. A package-consumer attempt
also exhausted the 8.8 GiB temporary volume ([log](temp-space-failure.log)); task-created
old consumers were removed and the baseline runtime moved to the workspace. Fresh
consumer validation then used the final packages. The retained final test
log is from the sequential run after a completed Rebuild.

| Check | Actual result |
| --- | --- |
| Debug / Release Rebuild | 0 warnings, 0 errors in both configurations |
| Core | 170 passed |
| Formulas | 833 passed |
| Mapping | 172 passed |
| Reader/OpenXml | 213 passed |
| Main | 836 passed |
| CLI | 105 passed |
| Total | **2,329 passed, 0 failed, 0 skipped** |

The conditional-formatting group contains 145 cases; Phase 2 adds 90 main cases
and two Reader/OpenXml cases compared with fetched main. Retained logs:
[Debug](debug-rebuild.log), [Release](release-rebuild.log), [tests](full-tests.log),
[Reader/Core isolation](reader-isolation.log), [pack](pack.log), [architecture checks](architecture-checks.log),
[package consumers](package-consumers.log), [package inventory](package-inventory.json).

Source architecture, namespace, Formulas BCL boundary and removed-PDF-dependency checks
passed. Reader and Core standalone consumers passed restore/build/publish, graph,
initial DLL, AssemblyRef and separate-process execution checks. Four local packages
were packed; Main/Mapping's shared DLL/XML hashes match. Isolated consumers exercised
the new sparse OpenXml API, resolved scale metadata and RGB(128,128,128) from a mapped
workbook using only packaged assemblies. Existing engine, adapter, Mapping, main+Core,
combined, font, namespace and installed CLI consumers also passed. No package was published.

## Independent expectations and visual artifacts

[expected-attributes.json](expected-attributes.json) is authored from small numeric
examples independently of the renderer output. [representative.xlsx](representative.xlsx)
is SDK-generated, not Excel-generated. Its four rows have six columns:

| Column | Rule / independent expectation |
| --- | --- |
| A | Duplicate values [10,20,20,30]: only the two 20s are yellow |
| B | Top 2 of [100,90,90,80]: 100 and both 90s are yellow |
| C | Mean of [0,10,20,0] is 7.5: 10 and 20 are yellow |
| D | 0 black, 50 green, 100 white: value 25 is RGB(0,128,0) |
| E | [0,1,2,100], percent 50 → threshold 50: value 1 is RGB(0,5,0) |
| F | Same data, percentile 50 → threshold 1.5: value 1 is RGB(0,170,0) |

Actual [PDF](representative.pdf), [PNG](representative.png) and [SVG](representative.svg)
are retained. PDF was independently rasterized with pdftoppm and visually compared
to PNG. The SVG was independently parsed: yellow fill count 7 and the green/percent/
percentile colors match the hand-authored values (including SVG's named/shorthand
color spellings). Tests assert shared fill/text drawing commands and rasterized SVG
page selection; no full-screen font-dependent pixel hash is used as an oracle.
Black text on black scale endpoints is preserved ordinary font behavior.

[mapped-scale.xlsx](mapped-scale.xlsx) contains [10,20,20,30] after row expansion.
Both 20s use RGB(128,128,128), proving the scale uses all expanded rows together.
Additional generated mapped PDF/PNG/SVG files and PDF review raster are under
`artifacts/conditional-formatting-phase2/`.

Hand-authored tests cover typed numbers/text/Boolean/date/error/blank/empty/space
keys under Turkish culture, first-duplicate matching, ties/percent ceilings, mean
and σ boundaries, large close values and overflow, percent versus percentile,
RGB endpoints/quarters, saved-anchor formulas, theme/indexed/tint colors, invalid/
collapsed thresholds, global fill priority/explicit clearing/stopIfTrue, selection/
hidden rows/print titles/merges, shared caches, missing off-selection formula caches,
whole-sheet implicit blanks, resource failures and cancellation. Mean-only and
frequency-only cases do not sort; multiple rules with equivalent normalized ranges
share one population and one numeric sort.

## Performance protocol and results

The baseline worktree was built at the fetched main SHA, outside the feature tree.
Its Release runtime was copied unchanged to `artifacts/conditional-formatting-phase2-baseline-runtime`
before removing the temporary worktree to free space.
The same harness references saved Release runtime directories and records DLL hashes
before/after. Run with no other builds, tests or rendering jobs overlapping:

```bash
python3 scripts/benchmark-conditional-formatting.py --phase2 \
  --before artifacts/conditional-formatting-phase2-baseline-runtime \
  --after tests/ExcelRenderer.Tests/bin/Release/net10.0 \
  --output artifacts/conditional-formatting-phase2-benchmark-final
```

Three alternating rounds, five warm-ups and three measured reads per process: nine
measured samples per case/product. Default runtime/JIT settings, one process at a time,
forced GC before each read. Timers/allocated bytes include workbook read/evaluation
and public model projection; layout/rendering are excluded. Mapping expansion occurs
before timed reads. Peak RSS is a process lifetime high-water mark including warm-ups,
not per-read managed live memory. Metrics report source workbook reads, shared snapshots,
population value reads, population builds, sorts and finite appearance candidates.

| Regression case | Main before ms | Feature after ms | Delta | Allocated MiB before → after |
| --- | ---: | ---: | ---: | ---: |
| No CF, 10k | 135.9 | 190.5 | +40.1% | 45.45 → 45.91 |
| No CF, 100k | 1042.8 | 1071.1 | +2.7% | 445.80 → 445.80 |
| Phase 1, 10k | 272.5 | 202.5 | -25.7% | 94.05 → 90.28 |
| Phase 1, 100k | 1615.0 | 1638.3 | +1.4% | 889.18 → 889.18 |
| Disabled, 100k | 1035.9 | 1098.7 | +6.1% | 445.89 → 445.89 |
| Core, 100k | 647.2 | 634.9 | -1.9% | 305.87 → 305.87 |

Largest observed regression-case median delta: +40.1%. These are measured medians; small-case JIT/timing variability does not establish a general speedup.

| Phase 2 read | 10k ms | 100k ms | Time ratio for 10× data | 100k allocated MiB | 100k peak RSS MiB | Populations / numeric sorts |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Numeric duplicate | 246.3 | 1663.7 | 6.75× | 836.03 | 286.62 | 1 / 0 |
| Text duplicate | 310.3 | 2048.5 | 6.60× | 1031.51 | 300.11 | 1 / 0 |
| Top N | 231.1 | 1431.8 | 6.19× | 757.25 | 278.18 | 1 / 1 |
| Above mean | 233.8 | 1539.6 | 6.59× | 794.57 | 282.89 | 1 / 0 |
| Three-color percentile | 248.3 | 1538.3 | 6.20× | 778.57 | 281.62 | 1 / 1 |
| Four rules, shared range | 288.0 | 1706.6 | 5.93× | 925.06 | 285.80 | 1 / 1 |

| Additional case | ms | Population value reads | Populations | Numeric sorts | Appearance candidates |
| --- | ---: | ---: | ---: | ---: | ---: |
| Full-column sqref / 10k stored values | 278.7 | 10000 | 1 | 1 | 10000 |
| Overlapping sqrefs | 205.3 | 10000 | 1 | 1 | 10000 |
| 16 distinct ranges | 250.8 | 159760 | 16 | 0 | 10000 |
| Saved formula values | 253.2 | 10000 | 1 | 1 | 10000 |
| Recalculated formula values | 281.4 | 10000 | 1 | 1 | 10000 |
| 10k Mapping-expanded rows | 282.4 | 10000 | 1 | 1 | 10000 |
| 5 appearance targets / 100k source values | 1358.1 | 100000 | 1 | 1 | 5 |

Selected and full reads have identical first-five colors. Selection limits appearance candidates; the public reader still materializes all stored model cells, so this case does not promise proportional read-time savings. No-CF/disabled/Core and Phase 1 cases build no Phase 2 populations or numeric sorts. Each reached Phase 2 case uses one shared calculation snapshot. Mean-only cases never sort. Numeric sorting is once per shared population. The 10×-data ratios show no quadratic growth in these fixtures.

[Summary and hashes](performance-summary.json) · [Raw final samples](performance-samples.json).

Fixtures include 10k/100k numeric and string values, individual and shared rules,
16 distinct populations, overlapping/full-column sqrefs, five visible cells with
100k source values, CachedValue/Recalculate formulas, and Mapping-expanded input.
The script checks independent fill counts and verifies identical colors for selected
and full views. Budget tests are separate from these normal performance cases.
The initial matrix failed its independent Mapping assertion (expected 10,000 colored
cells, actual 0). Mapping saved 10,000 separate row sqrefs, exceeding the initial
raw-range budget. The fix coalesces adjacent row/column intervals before the general
union sweep: up to 100,000 raw references are accepted, with 4,096 remaining sweep
rectangles and the existing work/output budgets. Tests now fix 10,000 expanded rows
across all rule types and 100,000 raw adjacent row intervals. The expected count was
not changed. [Initial failure](initial-mapping-failure.json), [log](initial-benchmark-failure.log)
and [raw initial samples](initial-benchmark-samples.json.gz) are retained separately;
only the corrected final matrix supplies the reported results.

Raw final samples, product hashes and fixture hashes accompany the summary. Timing variability
and the explicit read-only measurement scope do not establish whole-conversion or
cold-start performance, and these results are not a universal timing guarantee.

### Follow-up on regression-case timing variability

The default matrix recorded +40.1% for No CF 10k and +6.1% for disabled 100k. These values are retained above. Additional runs reused the **exact fixture bytes and product DLL hashes**, with no product-code changes. Warm-ups were increased because the small-input traces showed substantial JIT/process transitions. The last small-input run inherited `taskset -c 0` for both products; the ordinary executor allowed five CPUs. Runtime/JIT options were otherwise unchanged.

| Follow-up | Before ms | After ms | Delta | Per-round deltas |
| --- | ---: | ---: | ---: | --- |
| No CF 10k / 10 warm-ups | 131.5 | 145.0 | +10.3% | -2.4%, +27.5%, +11.2% |
| Disabled 100k / 10 warm-ups | 1058.2 | 1049.8 | -0.8% | -1.1%, -1.7%, +0.7% |
| No CF 10k / CPU 0 / 25 warm-ups | 336.6 | 349.6 | +3.8% | -14.1%, +1.9%, +62.0% |

**The 5% performance goal is not established for the small No-CF case in this environment.** Its pooled CPU-pinned median is +3.8%, but per-round changes range from −14.1% to +62.0%; the unpinned longer-warm-up median is +10.3%. These measurements cannot support a claim of no regression under default scheduling. No expected fill counts were changed to remove the failure. Disabled 100k did not reproduce its initial >5% median increase: its longer-warm-up rounds are all within ±2%. The principal 100k No-CF/Phase 1 medians were +2.7%/+1.4% in the default matrix. No general speedup, cold-start equivalence, or stable sub-5% small-input result is claimed.

Follow-up reproduction (use an empty output folder for each command):

```bash
python3 scripts/benchmark-conditional-formatting.py --phase2 \
  --before artifacts/conditional-formatting-phase2-baseline-runtime \
  --after tests/ExcelRenderer.Tests/bin/Release/net10.0 \
  --fixture-source artifacts/conditional-formatting-phase2-benchmark-final \
  --case plain-10000 --warmups 10 --measurements 5 --rounds 3 \
  --output artifacts/conditional-formatting-phase2-small-followup
# Disabled follow-up: same arguments, --case shared-100000 --mode disabled.
# CPU-pinned follow-up: taskset -c 0, --case plain-10000 --warmups 25.
```

[Small unpinned samples](small-followup-samples.json) · [Disabled samples](disabled-followup-samples.json) · [CPU-pinned samples](small-pinned-samples.json).

## Excel reference status and remaining limits

**No desktop Excel DisplayFormat capture or Excel-exported PDF comparison was performed.**
The optional [Windows capture script](../../../scripts/capture-conditional-formatting-excel.ps1)
was syntax-checked using PowerShell on Linux. COM execution remains untested. It uses
a temporary workbook copy, records Excel version/build/date/input SHA, captures
DisplayFormat and PDF, and closes Excel in finally. Run it separately on Windows:

```powershell
./scripts/capture-conditional-formatting-excel.ps1 -InputWorkbook ./docs/validation/conditional-formatting-phase2/representative.xlsx -OutputDirectory ./artifacts/excel-phase2-reference
```

Explicit profiles requiring Excel confirmation:

- Typed duplicate equality, especially text/number/Boolean, blank versus empty text,
  error strings and non-ASCII linguistic comparisons. OrdinalIgnoreCase is intentional.
- Percent count rounding (`ceil`), population σ (denominator n), percentile interpolation
  (PERCENTILE.INC profile) and exact numerical boundary behavior.
- Relative/mixed cfvo formula anchor semantics after Mapping; implementation evaluates
  one saved anchor, while combinations not preservable by Mapping still explicitly fail.
- sRGB byte interpolation, final round-away-from-zero, and collapsed scale intervals.
  All-equal min/max data and coincident middle/end thresholds are diagnosed and keep
  ordinary background. Full Excel compatibility is not claimed for those cases.

No Excel, Windows or LibreOffice observations are presented as an independent oracle.
