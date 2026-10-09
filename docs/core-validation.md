> Historical results from the Slim implementation before this refactor. These are not acceptance results for the Core migration. See [final refactor verification](core-refactor-validation.md), including current API, visual, package and performance checks.

# Core validation — 2026-10-08

Measured on Debian 13 / Linux x64, .NET SDK 10.0.401, runtime 10.0.12. Results describe this environment and fixtures, not a promised improvement for other workloads. [Raw observations](core-validation-results.json) retain all three isolated trials and all 50 repeated conversions.

## Build, correctness, and independence

- Solution Release rebuild: zero warnings / zero errors; locked restore succeeds.
- All 508 tests passed: 76 Core + 400 existing library + 32 tool tests. Core tests include API/streams/input limits, font format/snapshot/concurrency, baseline/underline/shrink/wrap, print areas/page order/empty sheets, anchors, shapes/links removal, transformed pictures, corrupt XLSX picture diagnostics, cancellation/Save failure, and cache leases.
- Sample builds and converts the Japanese fixture (2 pages). A new consumer under `/tmp/core-consumer` references only Core: net8.0 build passed; its conversion ran on the installed .NET 10 runtime with explicit major roll-forward. Native .NET 8 runtime execution is not verified here.
- Empty fontconfig with no OS font directories: the independent sample produced a 2-page Japanese PDF, confirmed through text extraction. No adjacent Fonts/Tool DLL is present in the consumer; no bundled font is embedded in Core.
- Product source has no OS/DLL font discovery, font-run fallback, shapes, link annotations, image crop/rotation/flip/split metadata, API range/viewport/continuous layout, or non-PDF renderer. ClosedXML receives the single-font graphic engine. Width and painting share conversion-owned regular XFonts.
- Observers and lease tests verify page payload active values 1/0, one Save, matching SKTypeface creation/disposal, and bounded image estimates/lease disposal. Product does not import PDFs or call GC.Collect.
- IVS follow-up: all 256 Unicode variation selectors are removed while neighboring code points and ordinary surrogate pairs are preserved. Cell/header reading and normalized layout caching are tested. Wrap and ShrinkToFit PDF glyph operators, origins, and sizes match base-only input, including headers and footers. Rebuild still has zero warnings/errors; TRX is under `TestResults/IVS`. Visual/performance measurements below precede this normalization follow-up and were not rerun for it.
- Four deliberate mutations (stored baseline +3pt, underline +3pt, image page boundary comparison, EMU divisor) each failed the corresponding numeric/page/anchor tests. Clean sources were restored and tests passed afterward. Logs: `TestResults/Core/Mutations`.
- Existing ExcelRenderer, Fonts, and Tool packages were packed locally. Core's IsPackable=false produces no nupkg; publish.yml is unchanged. No NuGet packages were published.

## Visual baseline

Nine existing SampleInputs were normalized to Noto Sans JP regular, with rotation, shapes, link annotations, and picture transforms removed. Reference and Core PDFs were generated in separate processes. Seventeen pages have equal page counts; rendered pages were inspected for Japanese text, images, wrapping, borders, merged cells, pagination, and clipping. Embedded TrueType streams and ToUnicode structure are also covered by tests. Header/footer handling inherits the original limited control-code handling; normalization can expose literal formatting control strings in these comparison inputs.

Rasterizer: Poppler pdftoppm 26.05.0, 96 DPI. Font SHA256: `d930d5d52d15231c283089760f84584272ad5e37e14607ba0d19c798e7a9caec`. The maximum reference/Core mean absolute channel difference (0–255 scale) was 0.000362245. Golden PNGs and renderer/font provenance are stored under `tests/ExcelRenderer.Core.Tests/Baselines`. PDF IDs, timestamps, and subset names are not compared as complete PDF bytes. The date/time field unit fixture uses the fixed timestamp 2026-10-08 12:34; the sample raster fixtures have no dynamic date/time fields.

The script checks both the reference output and stored Core golden images. CI records its installed Poppler version and uses a channel MAE tolerance of 0.25 for cross-version antialiasing. CI rasterizer installation is not pinned to the local 26.05.0 build; strict cross-environment rasterizer identity remains unverified. Geometry mutations are independently caught by PDF content/anchor tests.

## Performance

Two text columns, 18pt rows, A4, explicit Noto Sans JP regular, 1,000/5,000/10,000 rows. Image cases add the same 64×24px PNG every ten rows in column 2, within its row. Each renderer runs in a separate process, three trials per case, alternating full/Core. Times stop after conversion; RSS/managed sampling is process-wide and can include post-conversion PDF rereading used to validate page counts. No forced GC is used.

Each pair below is **full / Core**; ratios are **Core / full**. Values are medians. All page counts match. No elapsed-time or peak-RSS median worsened by 10% or more.

| Fixture | Rows | Seconds | Peak RSS MiB | Managed max MiB | Pages | PDF bytes | Time ratio | RSS ratio |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| text | 1,000 | 5.18 / 4.96 | 272.1 / 255.6 | 184.1 / 176.2 | 25 | 43483 / 44337 | 0.958 | 0.940 |
| text | 5,000 | 27.16 / 24.31 | 872.1 / 848.9 | 774.9 / 762.2 | 122 | 125594 / 129751 | 0.895 | 0.973 |
| text | 10,000 | 70.68 / 70.14 | 1618.7 / 1591.2 | 1508.1 / 1493.2 | 244 | 228700 / 237027 | 0.992 | 0.983 |
| images | 1,000 | 5.40 / 5.18 | 276.2 / 261.1 | 188.0 / 174.5 | 25 | 46134 / 46193 | 0.959 | 0.945 |
| images | 5,000 | 26.05 / 24.80 | 872.4 / 856.4 | 776.0 / 763.3 | 122 | 137696 / 138183 | 0.952 | 0.982 |
| images | 10,000 | 71.03 / 72.93 | 1623.8 / 1607.9 | 1515.1 / 1499.9 | 244 | 252686 / 253699 | 1.027 | 0.990 |

## Repeated conversions and limits

A separate process converted the 100-row, 2-column fixture 50 times (3 pages each). No collection was forced. After the first ten conversions, OS handle observations ranged from 100 to 104. End-of-conversion managed memory ranged from 29.4 to 61.8 MiB. RSS increased from 205.6 to 426.3 MiB over the observed period. This rise alone does not establish a leak, and these results do not prove the absence of long-term native/cache retention. SKTypeface disposal and image lease/cache disposal are separately tested; a complete native allocation/retention profile remains outstanding.

Windows execution is delegated to the added CI matrix and has not run in this Linux session. Skia's native default font manager can initialize fontconfig (the default configuration emitted cache-directory messages here). Selected faces still come only from the snapshot, and the fontless configuration test passes, but zero native fontconfig/filesystem initialization accesses is not an established structural guarantee. The strict native interpretation of “font exploration 0” is therefore unverified.

ClosedXML and the final PDF document remain retained during conversion. Large fixtures reached approximately 1.6GiB peak RSS; this implementation does not promise constant memory or a large RSS reduction.

## Reproduction

```bash
dotnet restore ExcelRenderer.slnx --locked-mode
dotnet build ExcelRenderer.slnx -t:Rebuild -c Release --no-restore --verbosity:minimal
source scripts/test-fonts.sh # only for the existing full-renderer regression tests
dotnet test ExcelRenderer.slnx -c Release --no-build --logger trx --results-directory TestResults/Final
dotnet build samples/ExcelRenderer.Core.Sample/ExcelRenderer.Core.Sample.csproj -c Release
python3 scripts/check-core-mutations.py
python3 scripts/validate-core.py # needs Poppler and Pillow; isolated full/Core comparison and 50 repeats
python3 scripts/validate-core.py --visual-only
```

Golden regeneration is explicit: `--accept-baseline`. Generated PDFs, PNG differences, JSON, mutation logs, and TRX are under TestResults/Core and TestResults/Final; CI uploads them. The harnesses are development tools, not product APIs or dotnet tools.
