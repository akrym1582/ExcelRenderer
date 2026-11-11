# Unwrapped outline bypass experiment

Baseline: draft PR [#115](https://github.com/akrym1582/ExcelRenderer/pull/115), commit `49a6be3461579eeedc32d0a2dfde0730f9f13106`. This baseline already includes the main rendering and Core glyph-storage changes. This experiment does not compare against bare main or the pre-#114 Core implementation. Earlier reports remain historical evidence.

## Scope and implementation

- Core `Pdf/SkiaTextMeasurer.Layout`: use the existing shared TextLineBreaker complete-line path when wrapping is disabled or width <= 0, bypassing OutlineParagraph construction, range enumeration and selection setup. `CreateLine` retains a cancellation check before glyph selection.
- Main `SkiaSharp/SkiaTextMeasurer.CreateLines`: select the existing complete-line path under the same effective no-wrap conditions. Resolve full lines through CreateLine, which preserves fallback, IVS, emoji, run boundaries, metrics and immutable GlyphSequence storage. Check cancellation before accessing a potentially cached line.
- Core owns normalization/newline splitting in TextLineBreaker. This still normalizes CRLF, splits explicit newlines and enumerates final lines; it does not eliminate every iterator or all text scans. No wrapping, cache, Paint or preflight algorithm is otherwise changed. Public API, dependencies, TFMs and package composition are unchanged.
- Added three main regression cases for wrap=false and wrap=true with zero/negative width: explicit CRLF, empty lines, trailing spaces, absence of outlineParagraphs and cancellation before a cached line. Existing fixed-seed/boundary-width Unicode/glyph/cluster oracles exercise Core and main, including no-wrap.

## Result and interpretation

No stable end-to-end speedup or material allocation reduction was established. The main text1000 controlled allocation change is approximately -0.08%; the Core no-wrap microbatch allocation slightly increases (about +0.006%). The intended bypass is observable, but this is retained as a separate, removable experiment, not evidence that the previous main text1000 regression is fixed. No other tuning was attempted.

Each cell below contains round 0 / 1 / 2 changes, after/before minus one. Negative means less time or allocation. Primary and controlled settings are separate.

| Product / case | Primary time | Primary allocation | Controlled time | Controlled allocation |
| --- | --- | --- | --- | --- |
| main / borders1000-Pdf | +6.64% / -9.17% / +9.46% | -0.08% / -0.08% / +0.13% | -1.26% / +0.51% / -3.91% | -0.08% / -0.56% / -0.56% |
| main / empty-Pdf | -10.03% / +0.48% / -5.33% | -0.00% / +0.01% / -0.00% | -1.24% / +6.62% / -9.90% | -0.01% / +0.00% / -0.00% |
| main / header-Pdf | -0.88% / +12.11% / -4.51% | -0.00% / -0.00% / +0.00% | -2.49% / +11.03% / -4.51% | -0.01% / -0.00% / -0.79% |
| main / imagesonly-Pdf | +5.64% / -20.20% / -19.41% | +0.01% / +0.00% / -0.79% | +11.83% / -33.63% / -30.87% | +0.00% / -0.79% / -0.01% |
| main / text1-Pdf | +15.15% / -34.04% / +5.78% | -0.00% / -0.00% / +0.00% | -6.84% / -10.42% / -5.03% | -0.77% / -0.00% / -0.01% |
| main / text1000-Pdf | +18.05% / +5.23% / -4.62% | +3.13% / -1.17% / -0.09% | +2.35% / -0.41% / +3.18% | -0.08% / -0.08% / -0.08% |
| main / wrap1000-Pdf | -11.36% / +3.51% / +7.33% | +0.04% / +0.07% / +0.09% | +1.28% / +1.31% / -1.50% | -0.00% / -0.07% / +0.00% |
| core / borders1000 | -7.86% / +8.32% / -14.67% | -0.39% / +0.01% / +0.04% | +11.73% / +28.81% / +11.51% | +0.01% / -0.42% / -0.39% |
| core / empty | -2.74% / +9.68% / +24.18% | +0.00% / +0.04% / +0.00% | +49.27% / -73.42% / -22.53% | +0.00% / +0.10% / -0.00% |
| core / header | +60.45% / +51.64% / -51.66% | -0.08% / -0.08% / -0.06% | +54.11% / +56.69% / -12.42% | +0.02% / +0.01% / +0.07% |
| core / imagesonly | +5.86% / +150.77% / +38.40% | +0.00% / -0.12% / +0.09% | +3.13% / +143.09% / +92.13% | +0.01% / -0.12% / -0.03% |
| core / outline-0 | -5.16% / +6.63% / -10.56% | +0.01% / +0.01% / +0.01% | -7.05% / +3.32% / +4.18% | +0.01% / +0.01% / +0.01% |
| core / text1 | +3.51% / +2.35% / +2.83% | +0.08% / +0.06% / +0.08% | -10.28% / -5.22% / +3.13% | -0.00% / +0.10% / +0.01% |
| core / text1000 | -17.53% / -4.91% / +5.96% | +0.03% / +0.05% / -0.38% | -1.21% / -19.80% / +19.55% | +0.56% / +0.48% / -0.46% |
| core / wrap1000 | +2.27% / -1.02% / +15.64% | -0.00% / +0.00% / -0.06% | -4.91% / +14.78% / +1.06% | +0.11% / -0.00% / -0.00% |

The Core outline-0 microcase uses the existing Core glyph harness with wrap=true and width=0 (effective no-wrap), 20 unique >2048-character inputs, and no layout-cache hits. Main layout-case plain/plain-wrap/rich-wrap output digests also match. That existing main long-text fixture includes combining text and compatibility runs; it is not a dedicated single-run fast-path benchmark. All cold/warm samples, GC collections and RSS are retained. RSS is process cumulative and includes validation/digest work outside measured intervals. Inclusive phase times are not summed.

Main text1000 diagnostic counts: outlineParagraphs 2000 -> absent (0), textGlyphCalls 2000 -> 2000, glyphStorageCopies 2000 -> 2000, Paint creation/disposal 1/1 -> 1/1. This removes outline bookkeeping without reducing glyph acquisition, final immutable storage or native Paint counts. There is no unbounded retention or new native owner.

Time/RSS excursions above 5% remain in several rounds, including unchanged small/image/wrap cases. `round-review.json` retains the flags; no pooled median is used to declare acceptance. The all-case 5% non-regression condition is not established. Windows was not executed.

## Validation

- Final Debug/Release solution Rebuild: 0 warnings / 0 errors. Final solution: 3076 passed (Core 463, main 1192, Formulas 833, Mapping 226, OpenXml 227, Tool 135).
- Core coverage: 455 tests, line 84.78% / branch 74.31%; Performance category: 8 passed. Coverage was collected with the same final Core source; the subsequent final source change affected main cancellation handling and its three tests only.
- Seven Core PDF pairs match page dimensions, pixels, text/glyph coordinates, drawings, images, links and embedded subset bytes. Main 24 PDF/PNG/SVG output files match through the existing structural/pixel or canonical-byte comparison.
- Reader/Core independent consumers and 19 Core/main comparisons at 144/288 dpi passed. Final fixed-PDF and public API/audit results are recorded in checks.json and logs.
- Existing scope disposal/replay, cancellation, output-error, fallback, rich-text, emoji and IVS tests are included in the full suite. No native handles were added to layouts.

## Reproduction and evidence

Use the same SDK/runtime and font setup recorded in environment files. Build the baseline commit in Release, save its Core.Validation and Performance runtimes, then apply product.patch and save final product assemblies in after-core/after-main. Use byte-identical benchmark executables for both stages. The benchmark scripts never build, restore, test or regenerate input bytes. Retained main inputs come from ../main-performance-optimization/inputs; Core inputs come from ../core-performance-optimization/inputs.

```sh
source scripts/test-fonts.sh
python3 scripts/measure-no-wrap-optimization.py artifacts/no-wrap-optimization
python3 scripts/measure-no-wrap-optimization.py artifacts/no-wrap-optimization --controlled
python3 scripts/validate-no-wrap-optimization.py artifacts/no-wrap-optimization
```

Three alternating sequential rounds per protocol. Whole-conversion processes have one cold iteration, two additional warmups and three measured warm trials. Main layout microbatches have three warmups and one measured batch per process, repeated over the three rounds; Core outline-0 has three warmups and three measured batches. Controlled disables tiered compilation; it does not replace primary results. Diagnostics/captures run separately after both timing protocols. Builds, tests, measurements and PDF raster verification were sequential. No earlier failed/prototype runtime is included in these comparisons.

Runtime/input/font hashes, SDK/runtime, OS/CPU, affinity and settings are retained in primary/controlled environment files; raw samples and per-round medians accompany them. source-state.json identifies the before commit and exact experimental source hashes. product.patch reconstructs only the two product-file edits and the additional regression cases against that commit. Root scripts reproduce timing and capture comparison. Saved executable runtimes remain under workspace artifacts/no-wrap-optimization; tracked evidence includes raw data, captures, logs, tests and coverage.
