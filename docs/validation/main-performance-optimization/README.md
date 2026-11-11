# Main rendering performance evidence — 2026-11-11

The baseline is main `a71e21d9f8a612c8747918383eced1742b385ada`, after PR #114.
See [implemented boundaries](../../main-performance-optimization.md)
([日本語](../../main-performance-optimization.ja.md)).

## Protocol

- Build and retain both Release runtime directories before measuring. They live
  under workspace `artifacts/main-performance/`; do not build/test/render concurrently.
- Apply `common-harness.patch` to the baseline before building its Performance
  executable. The patch adds plain-wrap/glyph-cluster digests and distinguishes
  current/max gauges; it does not change product algorithms.
- Reuse the retained `inputs/` bytes and fixed Noto font. The range fixture is a
  separately generated `Synthetic` sheet, matching the existing profile adapter.
- `environment.json` records OS/CPU/SDK/runtime, settings, all input/font/runtime
  DLL hashes. Whole conversions use the existing `render-profile`: one cold
  conversion, two additional warm-ups and three measured warm conversions, with
  harness collections outside conversion timing. Three process rounds alternate
  before/after order. `raw.json` preserves every sample and warm-up.
- `layout-samples.jsonl` preserves isolated batches, including three warm-up batches
  and one measured batch in each process. Its exact output digest includes run
  geometry, font identity, glyphs, source UTF-8, clusters and advances.
- `diagnostics.json` is separate: a warmed observer-disabled capture is compared
  with an observer-enabled capture for identical pages/output hash. Inclusive
  phase durations overlap and are not added to obtain wall time.
- Process peak RSS is a cumulative high-water mark across warm-ups/samples. It is
  not a reset per conversion and is assessed per process/round as well as samples.
- Prototype data and the first incomplete run with a mismatched range sheet are
  retained under workspace artifacts, excluded from final-runtime ratios.

## Reproduction

From the repository root, with fixed fonts and saved runtimes:

```sh
source scripts/test-fonts.sh
mkdir -p artifacts/main-performance
cp -a docs/validation/main-performance-optimization/inputs artifacts/main-performance/
python3 scripts/measure-main-optimization.py \
  --before artifacts/main-performance/before \
  --after artifacts/main-performance/after \
  --output artifacts/main-performance --prepare
python3 scripts/measure-main-optimization.py \
  --before artifacts/main-performance/before \
  --after artifacts/main-performance/after \
  --output artifacts/main-performance
python3 scripts/measure-main-optimization.py \
  --before artifacts/main-performance/before \
  --after artifacts/main-performance/after \
  --output artifacts/main-performance --diagnostics
python3 scripts/compare-render-finalization.py artifacts/main-performance
```

Completed checks and measurements follow.
Historical Core performance rates are not main results. An individual case/round
above 1.05 for time or RSS requires investigation; pooled medians do not establish
non-regression. Windows has not been executed in this Linux environment.

## Executed results

- Debug/Release solution Rebuild: 0 warnings / 0 errors.
- Whole solution: 3061 passed / 0 failed / 0 skipped.
- Core coverage: 443 passed; line 84.79% / branch 74.40%; Performance: 8 passed.
- Public API: main 2361 and Core 82 snapshot entries, unchanged.
- Saved main outputs: 24 files (12 PDF, 12 PNG/SVG) equal; font subsets/geometry also checked.
- Shared Core saved outputs: 7 cases equal. Supplementary wide-wrap PDF also equal.
- Existing PDF comparison: 25 fixtures, 0 unmet gates; frozen B has 0 differences.
- Main/Core: 19 direct comparisons at 144/288 dpi; 5 image fixtures passed.
- All 19 output mutations detected; shared geometry mutation detected by both products; source bytes restored.
- Namespace, dependency direction, publication allowlist, Reader/Core isolation and removed-dependency audit passed. Four local packages, eleven isolated consumers and font-package split checks passed. No project/package/TFM changes.

## Primary per-round results

These are combined implementation results. They do not attribute a time ratio to one feature. Allocation is the pooled median only as an allocation summary; time acceptance uses individual rounds.

| Case | Round 0 time change | Round 1 | Round 2 | Allocated after/before |
| --- | ---: | ---: | ---: | ---: |
| text1-Pdf | -1.7% | +2.2% | -4.2% | 1.0004 |
| text1000-Pdf | +21.2% | +8.4% | +9.5% | 0.9679 |
| wrap1000-Pdf | -9.7% | -16.8% | -22.3% | 0.8864 |
| borders1000-Pdf | -2.4% | +2.9% | -6.1% | 0.9306 |
| imagesonly-Pdf | +9.9% | -7.8% | -14.5% | 1.0001 |
| header-Pdf | +2.9% | -5.3% | +2.9% | 1.0004 |
| empty-Pdf | -1.0% | +12.5% | +9.0% | 1.0000 |
| mixed1000-Pdf | +5.5% | -6.7% | +7.5% | 1.0001 |
| 02-image-Png | -5.2% | +2.3% | -1.6% | 1.0004 |
| 02-image-Svg | -3.3% | -7.7% | +25.0% | 1.0004 |
| 04-text-decoration-Png | -0.8% | -6.4% | -2.7% | 1.0003 |
| 04-text-decoration-Svg | -1.4% | +32.4% | +4.0% | 1.0006 |
| 05-borders-Png | -11.4% | -13.9% | -7.6% | 1.0003 |
| 05-borders-Svg | +0.4% | -7.7% | +2.5% | 1.0003 |
| rich-Pdf | +14.8% | +3.2% | -10.4% | 1.0004 |
| rich-Png | +0.3% | -2.7% | +4.9% | 1.0005 |
| rich-Svg | +3.0% | +0.6% | +3.2% | 1.0005 |
| shapes-Pdf | +7.7% | -9.1% | +1.2% | 1.0005 |
| shapes-Png | +14.0% | -14.4% | -4.5% | 1.0006 |
| shapes-Svg | -14.9% | -11.1% | -14.2% | 1.0005 |
| range-trim-Pdf | -15.7% | +1.5% | +15.7% | 0.9995 |
| selected-Pdf | +3.6% | -13.9% | -11.5% | 0.9897 |

## Additional wide-wrap case

This is a separate, deliberately long ordinary-text workload, not a general speedup claim. `prepare-main-wide-wrap.py` retains 20 unique 3202–3203 UTF-16-unit cells and a source column width of 255. It uses the same saved runtime/font as primary trials.

- Per-round time changes: -94.93%, -96.22%, -96.97%.
- Allocations: 3687.76 → 54.34 MiB.
- Median GC counts (generations 0/1/2): [230, 0, 0] → [2, 0, 0].
- Layout GetGlyphs: 120120 → 40 calls; input units to those calls: 15412850 → 128100.
- PDF pixels, text/glyph coordinates, drawings, page dimensions and embedded subset bytes match.

## Structural observations

Baseline counter variants are used only for counts; their timings are excluded. All five baseline observer captures match the uninstrumented baseline bytes (canonical SVG where applicable).

| Case | Paint creation before → after | Layout GetGlyphs before → after | Full command builds before → after |
| --- | ---: | ---: | ---: |
| text1000-Pdf | 2000 → 1 | 4000 → 2000 | 50 → 25 |
| wrap1000-Pdf | 2000 → 1 | 64643 → 4000 | 50 → 25 |
| borders1000-Pdf | 5100 → 2 | 4000 → 2000 | 50 → 25 |
| rich-Pdf | 3 → 1 | 4 → 2 | 2 → 2 |
| 05-borders-Png (2 pages) | 41 → 4 | 26 → 9 | 4 → 4 |

The ordinary narrow wrap fixture has substantial old width-cache reuse: although glyph calls decrease, glyph input units increase from 634169 to 1165780. This is why call counts alone are not a time/complexity verdict. Every measured after context releases its created paints. Active page payload maximum remains one. The main layout cache reaches its 131072-unit budget; line payload maximum observed here is 103466 units, and entry maxima are 512.

## Limits and unresolved regressions

**The all-case time/RSS 5% non-regression criterion is not met by these samples.** Every primary flag is investigated in a separate tiering-disabled protocol (5 additional warm-ups, 8 warm samples, 3 alternating rounds). It does not replace primary trials. `text1000-Pdf` is slower by 8.4–21.2% in all three primary rounds; controlled time ratios are 1.090, 0.983, 1.080. The short/narrow wrap case improves in the primary protocol but has controlled time ratios 1.034, 1.017, 1.037. Other small/image/RSS cases also retain >5% excursions and direction changes. The evidence does not prove these are only noise, and no pooled-median pass is claimed.

Multiple-font linear wrapping, rich wrapping, native PNG/SVG wrapping, and full trim/range/diagnostic/CF/continuous/PS preflight remain compatibility paths. Paint reuse still has per-draw state-update/adapter costs. Font snapshot storage is outside the cache payload budget. Windows was not executed. Four local packages were packed and audited; eleven isolated package consumers, installed CLI font-format behavior and font-package split checks passed. Full hosted CI (including Windows) was not run.

## Retained artifacts

`source-state.json`, `product.patch`, `common-harness.patch`, `baseline-observation.patch`, `checks.json`, build/test/static logs, TRX files, coverage XML, raw samples, controlled samples, all input bytes and before/after captures are retained here. Paint counters cover command-drawing paints; unchanged SVG background and native-compatibility measurement paints are excluded on both sides.

Runtime binaries and prototype/incomplete-attempt evidence remain under workspace `artifacts/main-performance/`; runtime hashes are retained here. `product.patch` is cumulative against the fresh baseline and includes the common harness. Observer patch is a separate baseline variant.


Additional wide fixture (copy retained wide/inputs first to reuse the recorded bytes):

```sh
mkdir -p artifacts/main-performance/wide
cp -a docs/validation/main-performance-optimization/wide/inputs artifacts/main-performance/wide/
python3 scripts/prepare-main-wide-wrap.py artifacts/main-performance/wide
python3 scripts/benchmark-render-finalization.py \
  --before artifacts/main-performance/before --after artifacts/main-performance/after \
  --output artifacts/main-performance/wide --rounds 3
```

Follow-up regression investigation uses
`python3 scripts/recheck-main-optimization.py artifacts/main-performance`.
`validate-main-optimization.py` additionally expects observation-before and
core-before/core-after runtimes assembled from the corresponding baseline/final
builds; observation-before applies the separate retained observer patch.

The three retained patches were checked with `git apply --check` against a fresh archive of the baseline.
