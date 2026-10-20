# Saved-runtime measurements

Final product source: `ba4c377a459e06b6bfc93441ff2fb5815eb8d160`.
Baseline product source: `38390a69490fa56ca2ff28006d573eff5a3d925c`.
Each value below is the median of nine unobserved samples: three processes per stage,
eight warmups plus three samples per process. Order alternates by round, CPU 0, .NET
10, Release, identical input bytes and font settings. MiB = 1,048,576 bytes.
Cold conversions are retained separately. These pooled medians do not establish a
uniform latency bound; use the paired round medians/min/max in `final-summary.json`.

| Case | Before ms | After ms | Time Δ | Before MiB | After MiB | Allocation Δ |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| small-Pdf | 63.78 | 57.48 | -9.9% | 25.42 | 23.76 | -6.5% |
| small-PostScript | 65.17 | 61.91 | -5.0% | 35.71 | 34.24 | -4.1% |
| small-Png | 67.70 | 75.55 | +11.6% | 22.54 | 22.54 | -0.0% |
| small-Svg | 188.52 | 191.77 | +1.7% | 35.94 | 35.92 | -0.0% |
| ten-Pdf | 480.24 | 425.06 | -11.5% | 92.59 | 75.68 | -18.3% |
| hundred-Pdf | 1089.92 | 905.88 | -16.9% | 738.41 | 578.62 | -21.6% |
| hundred-PostScript-selected | 1161.19 | 1265.97 | +9.0% | 565.45 | 568.74 | +0.6% |
| borders-Png | 588.47 | 573.74 | -2.5% | 72.72 | 73.51 | +1.1% |
| repeated-Svg | 1530.52 | 1535.50 | +0.3% | 207.57 | 207.44 | -0.1% |
| unique-Pdf | 951.64 | 745.65 | -21.6% | 279.52 | 208.17 | -25.5% |
| range-trim-Pdf | 271.23 | 287.52 | +6.0% | 45.57 | 45.06 | -1.1% |
| continuous-Png | 512.96 | 415.58 | -19.0% | 89.26 | 88.73 | -0.6% |
| continuous-Svg | 1777.81 | 1796.75 | +1.1% | 160.57 | 160.05 | -0.3% |
| symbols-Pdf | 241.89 | 238.41 | -1.4% | 291.10 | 288.98 | -0.7% |
| symbols-PostScript | 297.88 | 296.73 | -0.4% | 329.79 | 328.24 | -0.5% |
| images-PostScript | 1593.83 | 1482.97 | -7.0% | 475.95 | 462.37 | -2.9% |
| large-image-Png-trim | 563.74 | 573.28 | +1.7% | 74.59 | 74.54 | -0.1% |
| rich-Pdf | 27.85 | 27.96 | +0.4% | 18.42 | 18.41 | -0.1% |
| rich-PostScript | 23.99 | 23.60 | -1.6% | 23.46 | 23.45 | -0.1% |
| rich-Png | 17.31 | 18.64 | +7.7% | 17.31 | 17.31 | +0.0% |
| rich-Svg | 17.73 | 20.57 | +16.1% | 17.38 | 17.38 | +0.0% |
| shapes-Pdf | 26.24 | 25.87 | -1.4% | 18.36 | 18.35 | -0.1% |
| shapes-PostScript | 23.46 | 23.70 | +1.0% | 23.42 | 23.41 | -0.0% |
| shapes-Png | 19.12 | 19.34 | +1.1% | 17.27 | 17.28 | +0.0% |
| shapes-Svg | 20.08 | 19.08 | -5.0% | 17.35 | 17.35 | +0.0% |
| read-plain-10000 | 353.44 | 432.83 | +22.5% | 51.15 | 51.15 | +0.0% |
| read-phase1-10000 | 510.13 | 533.18 | +4.5% | 102.38 | 89.32 | -12.8% |
| read-shared-10000 | 533.09 | 583.30 | +9.4% | 109.26 | 87.89 | -19.6% |
| read-plain-100000 | 1051.94 | 1043.39 | -0.8% | 451.04 | 451.04 | +0.0% |
| read-phase1-100000 | 1292.32 | 1313.27 | +1.6% | 894.41 | 763.90 | -14.6% |
| read-shared-100000 | 1420.03 | 1360.15 | -4.2% | 930.29 | 716.66 | -23.0% |
| read-disabled-100000 | 1125.77 | 1016.33 | -9.7% | 451.13 | 451.09 | -0.0% |
| read-selected-100000 | 1212.68 | 1125.43 | -7.2% | 578.56 | 578.54 | -0.0% |
| read-overlap | 586.52 | 502.51 | -14.3% | 123.15 | 87.89 | -28.6% |
| read-mapped | 774.35 | 647.84 | -16.3% | 172.51 | 112.38 | -34.9% |
| read-recalculate | 749.59 | 766.09 | +2.2% | 166.65 | 145.30 | -12.8% |

The primary `shapes-*` rows are retained seed-only cases, not actual shape evidence.
See the corrected shape-text supplement in the validation README; original data are
not silently renamed or replaced.

## Timing interpretation and bounded follow-up

The small PNG result at eight warmups is a measured +11.6% (paired rounds +10.8%,
+12.9%, +11.4%). It is not marked as a performance pass. A single targeted follow-up
used the same saved DLLs/inputs, 50 warmups, five samples, three alternating rounds,
with small SVG as a control. Small PNG then had paired changes +22.3%, -3.4%, -14.0%
and pooled -3.4%. Rich PNG changed -7.5%, +13.4%, +8.7%; small SVG +3.6%, +6.0%, +6.9%
(pooled +4.7%). Before-process small-PNG round medians ranged 66.71–81.50 ms, after
70.06–81.58 ms. These variations exceed the target, so latency remains inconclusive
for these small cases rather than a demonstrated 5% guarantee. No unlimited reruns
were used to select a favorable median; both complete protocols remain in evidence.

Separate matched-sink observed/control conversions preserve artifact identities and
page counts; PDF/PS/PNG bytes and sizes match exactly. SVG hashes use the existing
ReaderSnapshot resource-ID/reference normalization; raw bytes/sizes can change when
Skia increments its process-global clip IDs. Geometry/text/style are not normalized. The small-PNG diagnostic attributed less than 0.4 ms to common
finalization; reader time was about 31 ms and encoding about 19.5 ms in each stage.
Those samples do not explain an 8 ms product-code regression. A separate thread-stack
profile (`dotnet-trace` 10.0.750501, `dotnet-sampled-thread-time`, 20 warmups/20 samples)
shows reader and PNG encoding dominating both stages, with no newly dominant
finalization stack. It is a ~100 Hz thread-time sample of the whole diagnostic process,
including startup, forced GC and hashing, not a timed-window CPU regression proof.
The baseline sample also contains finalizer-thread time. Source/runtime configuration
and native Skia hashes match the prescribed stage inputs; platform scheduling/GC/JIT
variation cannot be separated further by these traces. Profile timings are excluded
from the table. See the retained inclusive/exclusive reports and compressed traces.

CF timings also vary by round. Allocation reductions are much more repeatable and
correspond to fewer candidate visits, ordered-list builds and dictionary/array copies.
Do not infer that CF caused the historical no-CF timing differences. Output sizes,
managed allocation, GC counts, process-lifetime peak RSS, cold samples and each round
are retained in the summary/raw files. The exact byte identities are in the input,
font, environment and product manifests.

## Corrected shape-text supplement

| Case | Before ms | After ms | Before MiB | After MiB |
| --- | ---: | ---: | ---: | ---: |
| actual-shape-text-Pdf | 31.40 | 31.04 | 19.40 | 19.38 |
| actual-shape-text-PostScript | 29.00 | 28.99 | 24.53 | 24.50 |
| actual-shape-text-Png | 28.93 | 28.33 | 18.12 | 18.20 |
| actual-shape-text-Svg | 27.94 | 28.13 | 18.96 | 19.03 |
