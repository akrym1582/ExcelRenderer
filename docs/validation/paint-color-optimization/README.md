# Paint RGBA setter optimization

Baseline: draft PR #115 commit `364d938512c93ac97fc492a571a91dbcff9800aa`, including the previous main/Core changes and no-wrap experiment. These results measure only the subsequent shared Paint-owner change, not bare main or pre-#114 Core. Earlier reports remain unchanged historical evidence.

## Implementation and lifetime

Core `Pdf/SkiaPaintResources.Fill` and `Stroke` each retain the last requested packed SKColor. All four byte components, including alpha, participate in equality. A matching request performs no native Color getter or setter. First creation initializes the requested color once, avoiding the previous duplicate setter. Width and dash/PathEffect updates still execute under the prior policy. Dispose clears the values; recreation and independent/nested drawing owners keep separate state.

The same Core owner is already used by main PDF/PNG/SVG; main adds no copied color-cache algorithm. There is no global paint, lock or color dictionary. The additional managed payload is two 4-byte values per owner, with no new native resource or public-layout handle. Microbatch allocation increases by exactly 8 bytes per newly created owner: fill 192 -> 200 bytes, stroke 216 -> 224 bytes, across 300000 requests. This is not a per-request allocation.

Main SkiaTextDrawing temporarily sets a borrowed fill to white for native color emoji and restores its native original value in finally. That existing temporary getter/two-setter sequence remains unchanged and is excluded from the owner counter totals. It completes before another owner request; the shadow RGBA therefore remains valid. A comment documents this requirement. Other owner-color mutation paths were searched, including ColorF, SetColor and Reset. SVG page-background paints and measurement paints are outside this owner and remain unchanged.

The observer is read once per Fill/Stroke request, keeping disabled-instrumentation cost bounded. Fill has an AggressiveInlining hint after its body grew; actual generated-code inlining was not separately disassembled. The measured implementation includes these management-cost changes, so gains are not attributed to setter suppression alone. All-change fill can improve through this implementation even though it skips almost no setters.

## Measured setter calls

These are explicit SKPaint.Color setter calls in shared-owner initialization and requests. Before calls are reconstructed from observed paintFillRequests + paintStrokeRequests + paintCreated: the baseline writes unconditionally for every request and once in CreatePaint. After calls are directly counted by paintColorSetters. Both stages have identical request counts and balanced native creation/disposal. This is source-backed call accounting, not interception of every native Skia call in the process.

| Product / case | Before setter calls | After setter calls | Paint creations before / after |
| --- | ---: | ---: | ---: |
| core / text1000 | 2001 | 1 | 1 / 1 |
| core / borders1000 | 5102 | 51 | 2 / 2 |
| main / text1000-Pdf | 6001 | 1 | 1 / 1 |
| main / borders1000-Pdf | 9102 | 51 | 2 / 2 |
| main / 05-borders-Png | 71 | 22 | 4 / 4 |
| main / 05-borders-Svg | 71 | 22 | 4 / 4 |
| main / rich-Pdf | 5 | 3 | 1 / 1 |

paintColorSettersSkipped counts matching requests after creation. Avoiding the duplicate setter at creation removes one additional baseline call per Paint. After setters + skipped requests equals owner requests; the counter name alone does not include that initial duplicate removal. Counters run separately from primary timing. Main diagnostic/control captures match, and Core diagnostic PDFs match their observer-free primary counterparts.

## Time and allocation

Each cell below lists round 0 / 1 / 2 changes, after/before minus one. Negative is less time/allocation. Primary and tiering-disabled protocols are separate.

| Product / case | Primary time | Primary allocation | Controlled time | Controlled allocation |
| --- | --- | --- | --- | --- |
| micro / micro-fill-alternate | +1.26% / -23.50% / +6.22% | +4.17% / +4.17% / +4.17% | -33.70% / -26.20% / -34.56% | +4.17% / +4.17% / +4.17% |
| micro / micro-fill-repeat | -47.31% / -52.55% / -53.08% | +4.17% / +4.17% / +4.17% | -66.52% / -66.86% / -67.01% | +4.17% / +4.17% / +4.17% |
| micro / micro-stroke-alternate | +6.80% / +10.05% / +2.10% | +3.70% / +3.70% / +3.70% | +9.61% / -0.79% / -2.03% | +3.70% / +3.70% / +3.70% |
| micro / micro-stroke-repeat | -14.23% / -9.64% / -13.15% | +3.70% / +3.70% / +3.70% | -22.99% / -22.96% / -22.55% | +3.70% / +3.70% / +3.70% |
| core / borders1000 | +17.96% / -9.62% / +2.86% | -0.01% / +0.02% / +0.00% | +1.97% / -9.14% / +21.77% | -0.01% / +0.46% / -0.00% |
| core / empty | -41.56% / -58.21% / +111.25% | +0.02% / +0.07% / +0.08% | -85.90% / +25.93% / -67.06% | +0.07% / +0.03% / -0.00% |
| core / header | -63.96% / +0.93% / +6.59% | +0.06% / -0.01% / -0.04% | +16.71% / -41.41% / -9.51% | +0.04% / +0.10% / +0.02% |
| core / imagesonly | +153.32% / +99.12% / -25.40% | -0.00% / +0.04% / -0.09% | +3.35% / +38.08% / -61.89% | +0.00% / +0.02% / +0.00% |
| core / text1 | +1.34% / -11.51% / -1.33% | +0.02% / -0.06% / +0.01% | -39.24% / +18.71% / -4.93% | +0.06% / -0.03% / -0.01% |
| core / text1000 | -10.34% / -7.95% / -14.65% | +0.43% / -0.01% / +0.00% | -28.95% / -1.78% / +2.33% | +0.00% / -0.00% / -0.02% |
| core / wrap1000 | +7.50% / +9.04% / +6.26% | -0.00% / -0.12% / +0.00% | +0.15% / +2.67% / -6.82% | +0.12% / -0.01% / +0.00% |
| main / 05-borders-Png | +1.71% / +11.65% / -11.14% | +0.00% / -0.00% / +0.01% | +3.87% / -9.31% / +1.58% | +0.00% / +0.00% / -0.01% |
| main / 05-borders-Svg | +17.20% / -9.17% / +10.35% | -0.01% / +0.00% / +0.03% | +6.72% / -4.41% / -2.94% | -0.02% / -0.01% / -0.01% |
| main / borders1000-Pdf | +7.77% / -10.27% / +2.99% | +2.02% / +0.48% / -0.16% | -3.21% / +1.14% / -1.53% | -0.00% / -0.00% / -0.49% |
| main / empty-Pdf | +0.87% / -3.39% / +3.16% | -0.00% / -0.01% / +0.00% | +1.84% / -14.48% / -3.82% | -0.00% / -0.00% / +0.00% |
| main / header-Pdf | +0.91% / +3.83% / +3.02% | +0.00% / +0.00% / +0.01% | +7.74% / -10.32% / +3.07% | -0.00% / -0.78% / +0.01% |
| main / imagesonly-Pdf | -51.61% / -5.39% / +0.70% | -0.79% / +0.00% / +0.00% | +0.30% / +5.82% / -0.75% | +0.00% / -0.00% / -0.00% |
| main / rich-Pdf | +3.42% / -4.34% / -2.11% | -0.01% / +0.02% / -0.00% | +102.93% / +12.37% / -4.89% | +0.75% / -0.01% / -0.00% |
| main / text1-Pdf | -2.78% / +5.99% / -0.38% | -0.79% / -0.00% / -0.00% | +3.29% / +10.79% / +12.28% | +0.79% / +0.80% / -0.01% |
| main / text1000-Pdf | -7.08% / +8.71% / +1.18% | -0.52% / +0.53% / -0.00% | -0.54% / -8.92% / +3.98% | -0.00% / +0.00% / -0.00% |
| main / wrap1000-Pdf | +8.37% / -3.05% / -1.21% | -0.02% / +0.04% / -0.04% | +2.22% / +2.06% / +3.89% | +0.07% / -0.07% / -0.00% |

Repeated fill requests improve 47–53% in primary microbatches, repeated stroke about 10–14%. Every-change stroke has some primary regressions of about 2–10%; these are retained as management-cost evidence. Micro workloads isolate owner requests from layout and actual canvas drawing; stroke still updates width and checks dash state. Allocation is nearly unchanged apart from the fixed 8-byte owner payload. This is not an overall converter improvement rate.

Whole-conversion time/RSS still vary. Main text1000 primary changes are -7.1% / +8.7% / +1.2%; controlled changes are -0.5% / -8.9% / +4.0%. Core text1000 primary changes are -10.3% / -8.0% / -14.7%, while controlled changes are -29.0% / -1.8% / +2.3%. These settings do not establish a consistent general speedup or resolve the older main text1000 regression. The all-case time/RSS 5% non-regression condition is not established. Per-round time/RSS flags, all raw cold/warm samples, allocations and GC counts remain in round-review.json and the protocol files. No pooled-median pass is used. Windows was not executed.

## Validation

- Final Debug/Release solution Rebuild: warning 0 / error 0. Full solution: 3080 passed (Core 467, main 1192, Formulas 833, Mapping 226, OpenXml 227, Tool 135).
- Core coverage: 459 non-Performance tests, line 84.84% / branch 74.33%, gate passed. Performance category: 8 passed.
- Four new Core cases verify all RGBA channels, repeat/changed colors, fill/stroke separation, width/dash resets, independent owners, Dispose/recreation and temporary emoji-color restoration through an exception. Existing pixel/raster, emoji, partial-formatting, fallback, IVS, cancellation, output-error and post-scope public-layout replay tests remain in the full suite.
- Main 24 PDF/PNG/SVG files and Core 7 PDF pairs match. Core additionally has four diagnostic/control PDF comparisons. PDF comparisons cover page dimensions, pixels, text/glyph positions, drawings, images, links and embedded subset bytes.
- Fixed PDF suite: 25 fixtures, 0 unmet gates; frozen B comparison has 0 differences. Its reviewed A differences are byte-identical to the prior no-wrap retained data, referenced rather than copied again.
- Reader/Core isolation and 19 direct Core/main comparisons at 144/288 dpi passed. Namespace/dependency/prohibited-dependency audits passed. Public API is unchanged (main 2361 / Core 82 entries).

The audit detected legacy-backend wording in the previously retained no-wrap fixed-PDF differences JSON. Only that immutable data file is registered by exact SHA in the existing historical-evidence manifest. Its bytes and prior artifact hashes remain unchanged. The audit code, source allowlist, dependency graphs, AssemblyRef checks and package checks are unchanged.

## Protocol and reproduction

Use identical input/font bytes and saved Release product runtimes. Apply common-harness.patch to both stages: it adds paint-color-case and its dispatch, using existing internal Fill/Stroke signatures. Both stages run byte-identical saved Performance and Core.Validation executables; only final product assemblies differ. product.patch applies the two product-file changes and four regression cases to the baseline commit. Do not build or test while measuring.

```sh
source scripts/test-fonts.sh
python3 scripts/measure-paint-color.py artifacts/paint-color-optimization
python3 scripts/measure-paint-color.py artifacts/paint-color-optimization --controlled
python3 scripts/validate-no-wrap-optimization.py artifacts/paint-color-optimization
```

The existing no-wrap-named capture validator is reused without changes as a generic saved-runtime output comparator. Main fixture bytes come from ../main-performance-optimization/inputs, Core bytes from ../core-performance-optimization/inputs. Three alternating sequential rounds per protocol; each process has one cold iteration, two additional warmups and three measured warm trials. Paint microbatches make 300000 requests per iteration. Creation is timed, native getters/digest and disposal occur after timing. RSS is process-cumulative and includes those excluded operations. Inclusive phase times are not summed. Whole-conversion Core font loading remains within its existing harness timing contract.

Primary and controlled samples are observer-free. Diagnostics, PDF raster verification, builds and tests run sequentially outside timing. Metadata records SDK/runtime, OS/CPU/affinity, settings, input/font/DLL hashes, warmups and each sample. Runtimes remain in workspace artifacts/paint-color-optimization; source-state.json identifies the baseline and final source hashes. The discarded initial prototype had two observer lookups per request and lacked the inline hint; its data/source/runtime snapshot remain under prototype-initial in workspace. The probe was used for implementation selection only. Neither is pooled into final measurements. Native color-call interception, JIT disassembly and Windows execution were not performed.
