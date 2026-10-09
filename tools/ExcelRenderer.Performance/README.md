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
`rowstyle` applies whole-row/column formatting; ClosedXML may expand many cells.

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
python tools/ExcelRenderer.Performance/prepare-baseline.py /tmp/excel-baseline
dotnet build /tmp/excel-baseline/tools/ExcelRenderer.Performance/ExcelRenderer.Performance.csproj -c Release
python tools/ExcelRenderer.Performance/run.py /tmp/excel-baseline/tools/ExcelRenderer.Performance/bin/Release/net10.0/ExcelRenderer.Performance.dll /tmp/table.xlsx /tmp/results/baseline
```

The exporter requires the investigation commit to be present in local git history.
It verifies source fragments before applying the measurement-only overlay; it does
not copy optimizations. Use exactly the same generated XLSX and font for both runs.
Start with 10 rows: the original implementation exceeded the 8 GiB container limit
on larger text fixtures. See [the report](../../docs/pdf-performance.ja.md).

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
