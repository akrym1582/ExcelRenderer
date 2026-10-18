# Rendering performance harness

Requires .NET 10. Run from the repository root. The library remains netstandard2.1.

For XLSX template mapping, use the independent Linux harness:

```bash
python tools/ExcelRenderer.Performance/mapping-benchmark.py --output /tmp/mapping.jsonl --runs 1
python tools/ExcelRenderer.Performance/mapping-benchmark.py --output /tmp/mapping-clr.jsonl --mode clr --rows 99998 --runs 3
```

It builds a temporary .NET 10 executable referencing `ExcelRenderer.Mapping`, then
runs each case in a fresh process. Modes: `clr`, `json`, `dict`, `block`, `nested`,
`merged`, `merged-contained`, `formula`. CLR fixtures have 10 properties; other modes accept
`--columns`. Nested fixtures have 10 items per parent and require a row count
divisible by 10. Templates contain a header and footer, shared cell formatting,
and a custom row height. The 99,998-item case reaches the default 100,000-row
limit; the 100,000-item case checks rejection without writing output.

Mapping time includes template loading, expansion, formula evaluation, XLSX saving,
and copying to the output stream. Input preparation and output validation are
outside timing. The runner samples process RSS every 10 ms until mapping returns;
this peak includes startup and input preparation. Managed allocation bytes are
cumulative during mapping, not peak memory. Each output is reopened to check row
count, every data cell's value/type, footer, row heights, formatting, and all merge
addresses as applicable. `formula` checks rejection of formulas in repeated rows
without writing output.
Records append to the requested JSONL path; metadata is written alongside it.
`--timeout` defaults to 90 seconds per process, including output validation.
Timeouts and validation failures are recorded and make the runner exit nonzero.
`merged` covers the fixed regression where a merge extends beyond the rightmost
used cell. `merged-contained` adds a regular cell after the merge; both fixtures
validate every merge.
See [mapping measurements](../../docs/mapping-performance.ja.md).
See [the improvement investigation and implementation plan](../../docs/mapping-performance-plan.ja.md)
for phase profiles, isolated prototypes, compatibility limits, and failed-test fixes.

For direct PNG/SVG rendering, use the same Release build with:

```bash
dotnet tools/ExcelRenderer.Performance/bin/Release/net10.0/ExcelRenderer.Performance.dll direct-images /tmp/image-results png text 3
```

Formats: `png`, `svg`. Workloads: `text` (20 repeated Japanese/Latin text commands per
page), `shapes` (10,000 colored rectangles per page), `large` (4096×4096 PNG pixels).
Each call renders three pages; the last argument repeats the call in one process.
Text and shapes use 1200×1200 pixels/points at 72 DPI. The large workload is intended
for PNG. Parsing and layout are excluded, fonts are explicitly registered and resolution
is warmed before measurement. Reports include elapsed time, managed allocations,
process lifetime peak working set, native face creation counts and per-page hashes.
Peak working set includes initialization and grows across repeats; allocations are
cumulative rather than peak memory. See [measurements and limitations](../../docs/image-performance.ja.md).

The remaining instructions cover the XLSX-to-PDF benchmark.

Generated worksheets contain synthetic labels only. Input preparation and PDF inspection
are separate from conversion timing; JSON records conversion time and allocations,
phase timings and end snapshots, GC generations, ZIP expansion and model counts,
PDF object/stream counts, and the input/font SHA256.

```bash
dotnet build tools/ExcelRenderer.Performance/ExcelRenderer.Performance.csproj -c Release
dotnet tools/ExcelRenderer.Performance/bin/Release/net10.0/ExcelRenderer.Performance.dll generate /tmp/table.xlsx 1000 text
python tools/ExcelRenderer.Performance/run.py tools/ExcelRenderer.Performance/bin/Release/net10.0/ExcelRenderer.Performance.dll /tmp/table.xlsx /tmp/results/table
```

Generator modes: `text`, `empty`, `latin`, `bold`, `wrap`, `merges`, `far`,
`rowstyle`, and `mixed`. All use 20 columns. Repeat with 5000 and 10000 rows.
`mixed` adds repeated titles, multiple print areas, a second paper size, an internal
hyperlink, and an image crossing a page boundary. `far` preserves a distant styled
blank cell and a row-height override outside the explicit print area.
`rowstyle` applies whole-row/column formatting without expanding the complete grid.

The Linux Python runner launches three fresh processes by default and samples RSS
and private resident memory every 10 ms. It saves JSONL, stderr phase snapshots,
sampled peaks, and one PDF per run. Nonzero exits still save sampled peaks.
The sampled process peak includes harness initialization and PDF inspection;
conversion-only working-set snapshots and allocations are recorded separately.
Private resident memory from smaps is distinct from .NET's PrivateMemorySize64.

Fonts are explicitly registered from `third_party/NotoSansJP/NotoSansJP-Regular.ttf`.
System and optional packaged fonts are disabled for a stable comparison.
Override the font using `--font path.ttf`. To test the same process ten times:

```bash
python tools/ExcelRenderer.Performance/run.py tools/ExcelRenderer.Performance/bin/Release/net10.0/ExcelRenderer.Performance.dll /tmp/table.xlsx /tmp/results/repeat --runs 1 --repeat 10
```

Only this harness forces GC after repeated conversions, outside conversion timing.
It records post-GC managed bytes, RSS and private bytes. Production conversion does
not force GC. Native font-strike cleanup is part of production conversion timing.

To reproduce the original implementation with the same instrumentation:

```bash
# Historical source injection has been retired; build the recorded first-stage Git commit in a separate temporary checkout.
dotnet build /tmp/excel-baseline/tools/ExcelRenderer.Performance/ExcelRenderer.Performance.csproj -c Release
python tools/ExcelRenderer.Performance/run.py /tmp/excel-baseline/tools/ExcelRenderer.Performance/bin/Release/net10.0/ExcelRenderer.Performance.dll /tmp/table.xlsx /tmp/results/baseline
```

The historical investigation and its original instrumentation are recorded in
[the report](../../docs/pdf-performance.ja.md). Build historical code only in a
separate checkout; the normal solution does not regenerate it.

For system-font PNG/SVG tests in this Linux image, Fontconfig needs the bundled
Japanese font directory and must exclude the image's unsupported OpenAI WOFF2 fonts.
The report provides the test configuration. No production font defaults are changed.

For an optional strict raster comparison, install PyMuPDF in your Python environment
and run the following against the generated PDFs. It compares every page at 96 dpi,
reports input hashes and differing page numbers, and exits nonzero on a difference.
PDF structure and hyperlinks are checked separately by the .NET regression suite.

```bash
python tools/ExcelRenderer.Performance/compare-pdf.py /tmp/results/baseline-0.pdf /tmp/results/final-0.pdf
```

The integration worker always uses OpenXml. Its current invocation is
`integration-worker <input.xlsx> <operation> <row-count> [mapping-capture.xlsx]`;
there is no backend argument. See the [migration guide](../../docs/engine-selection-removal.md)
for the updated benchmark/profiling scripts and baseline CLI output comparison.

## PDF dependency-removal measurements

```bash
source scripts/test-fonts.sh
python3 scripts/benchmark-pdf-backends.py --fixture all --output artifacts/pdf-removal-benchmark
```

The current harness accepts `--pdf-backend skiasharp` on `render-case`:

```bash
dotnet run --project tools/ExcelRenderer.Performance -- render-case samples/excel/japanese-grid-report.xlsx pdf paginated 100 --font-pack --measure-text --pdf-backend skiasharp
```

The performance project includes the optional Fonts assembly. The comparison
script enables it for both stages. It defaults to Release, three warm-up
conversions per fresh process, five measured rounds with alternating forward/reverse stage order, and process concurrency
1/4/8. Seven fixtures cover invoice, Japanese grid, rich text, borders, 32 distinct
256px PNGs (16 with alpha), and exactly 50/200 pages (asserted at runtime).
Raw records retain time, process lifetime peak RSS, allocations, bytes, phase
measurements and resource counts. Median and maximum are reported; B-relative
time/RSS target 1.25. `--generate-only` prepares inputs without measurement.

C is measured by default. To compare against frozen stage B, manually build the
recorded first-stage commit in a separate temporary checkout and supply
`--baseline-assembly /tmp/stage-b/ExcelRenderer.Performance.dll`. There is no normal
CI/build dependency on that checkout. For measurement-phase instrumentation,
compile the current `RenderMemoryBenchmark.cs` into a temporary friend executable
named `ExcelRenderer.Performance`, referencing the unchanged stage-B product DLLs
and their original managed/native dependencies. `--measure-text` wraps the
existing conversion-local measurer, delegates all three interfaces and records
`textMeasure.ms`; it does not replace the cache or layout algorithm. Record both
harness hashes and verify the old main/Core DLL hashes before comparing.
`--include-reference-a` requires that explicit historical assembly and is optional.

Do not overlap formal measurements with builds, comparisons or package consumers.
Process concurrency does not establish in-process throughput; separate mixed
Core/main tests exercise scope isolation at 1/4/8. The 100-repeat command records
post-GC memory outside timed conversion, and creation/disposal counts distinguish
native cache high-water marks from resources retained per conversion. See
[the removal validation](../../docs/pdfsharp-removal-validation.md) for results,
conditions, exceptions and the exact baseline provenance.

Use `--inputs artifacts/pdf-removal-benchmark` to reuse the exact generated workbook bytes for controlled reruns; every selected input hash is recorded. Initial grouped-stage measurements remain separate evidence when the protocol is corrected.


## Follow-up performance review

`layout-case plain|rich|rich-wrap [count]` isolates unique text layout from input,
drawing and saving. It uses one conversion font scope per batch, three warm-up
batches and one measured batch, explicit Noto Sans JP and no system/font-pack
lookup. `plain` uses about 3,200 UTF-16 units per cell; `rich` uses two such style
segments; `rich-wrap` uses shorter mixed-size text at 80pt. Default batch size is
30. Unique prefixes and the long cases avoid relying on repeated whole-layout
cache hits. Font discovery, result hashing and scope disposal are outside this
microbenchmark's timer. End-to-end `render-case` includes conversion teardown.

```bash
source scripts/test-fonts.sh
dotnet tools/ExcelRenderer.Performance/bin/Release/net10.0/ExcelRenderer.Performance.dll layout-case rich 30
python3 tools/ExcelRenderer.Performance/review-performance.py \
  --before /workspace/pdf-removal-review-before/ExcelRenderer.Performance.dll \
  --after tools/ExcelRenderer.Performance/bin/Release/net10.0/ExcelRenderer.Performance.dll \
  --inputs artifacts/pdf-removal-benchmark --output artifacts/pdf-review
```

Save the unchanged before-product DLLs and dependencies outside the repository
before rebuilding. Copy the newly built benchmark DLL/PDB (only the harness) into
that directory so both products run identical instrumentation. The review runner
records hashes, inputs, five fresh-process rounds, three warm-ups and alternating
before/after order. It covers seven PDF workloads at concurrency 1, rich text at
4/8, all three layout microbenchmarks, and direct PNG/SVG text output. It records
all samples, medians and maxima without claiming a universal speedup. Layout
text/style/dimensions/origins hashes and direct-image bytes must match. Independent
PDF/regression comparisons remain required. `--only layout-` selects a focused
investigation without generating new input. See [the follow-up review](../../docs/rendering-performance-review.md).
