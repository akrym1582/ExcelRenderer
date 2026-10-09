# Core refactor verification — 2026-10-08

Baseline: `028b6214495eb6f6a30a29990d5b6f86f025c502`, retained in a detached worktree.
Work branch: `refactor/core-engine`. No commit, tag, push or publication was performed.

## Architecture and migration

The conversion engines are `ExcelRenderer` and `ExcelRenderer.Core`; the existing Fonts,
Tool, samples and two engine test projects remain. There is no compatibility Slim DLL
or third engine project. Core is netstandard2.1, nonpackable, and does not reference
main/Fonts/Tool. Dependency versions and published package IDs are unchanged.

Core owns workbook/cell traversal, original basic style/metadata reading and interning,
input preparation/ZIP limits/spooling/cancellation, image resources, column/axis/band
geometry, print-area and page planning, candidate extraction, page-local measurements,
cell/border/text placement, cell-layer drawing, diagnostic aggregation, and PDF
append/basic painting/image decode/Save. Main's public models and facades remain in
`ExcelRenderer.dll`. FullExcelReader/FullLayoutPolicy/FullDrawingExtension/FullPdfRenderer
supply full styles, original text, shape/link geometry, selection, image intersections
and transforms, finalized runs, shapes, viewports and annotations. PNG/SVG/Markdown,
continuous output, preflight, trim and artifact orchestration remain in main and use
the common basic calculations. See [source ownership](core-source-map.md) and the
[English](core-architecture.md)/[Japanese](core-architecture.ja.md) architecture.

Normal rendering passes neutral reader dictionaries directly to layout; public sheet
views borrow those values. Public reader results materialize durable public dictionaries
for compatibility. Publicly constructed sheets use ordered projection views and style
caches. Page/command adaptation stays page-local; image/font bytes are borrowed. Typed
line payloads preserve finalized runs and metrics without new wrapping/font searches.

One cancellable PDFsharp gate is shared by both converters and independent public
measurement/rendering in the same load context. Internal paths borrow the conversion
session. Native resources are disposed before resolver cleanup and gate release, including
failure/cancellation. Font snapshots are owned by resolved fonts/public finalized layouts;
global registrations hold weak references and expired entries are cleaned. This does
not synchronize unrelated PDFsharp clients or independent AssemblyLoadContexts.

To migrate a former Slim source consumer:

```xml
<ProjectReference Include="../src/ExcelRenderer.Core/ExcelRenderer.Core.csproj" />
```

```csharp
using ExcelRenderer.Core;
```

Type simple names, signatures and stream/exception contracts are retained. Old Slim
binary compatibility is intentionally not provided. Main NuGet consumers install only
`ExcelRenderer`; its package includes the same-build Core DLL/XML. Do not mix DLLs
from different builds. The optional Fonts package and Tool keep their existing roles.

## Executed checks

Environment: Debian 13, .NET SDK 10.0.401/runtime 10.0.12. Both baseline and final
PDFs use Poppler pdftoppm 26.05.0 at 96 DPI, identical font bytes and fixtures.

The shared axis mutation was detected by both products; four Core and nineteen main
mutations were detected by failing regression tests and all sources were restored. See
[mutation results](core-refactor-mutation-results.json). Final build, package and output
checks are listed below.

| Check | Final result |
| --- | --- |
| Restore and locked restore | Passed |
| Solution Release rebuild | 0 warnings / 0 errors |
| All regression tests | 518 passed: Core 76, main 410, Tool 32; 0 skipped |
| Core sample build | Passed, 0 warnings / 0 errors |
| Public/nullable API comparison | Main 2,174 / mapped Core 85 entries; 0 differences |
| Main, Fonts and Tool pack | All three passed; no Core package added |
| Nupkg contents | Core DLL/XML included in main and Tool; no Core package dependency; main retains five external dependencies |
| Isolated main-only package consumer | Japanese PDF/PNG/SVG/Markdown passed |
| Old main consumer DLL | Unchanged binary passed all four formats with upgraded DLLs/dependency manifest |
| Independently installed Tool | Japanese PDF passed from the local package |
| Independent Core ProjectReference | Japanese PDF passed, only Core engine DLL present, empty fontconfig directories |
| Dependency/ownership guard | Passed; no Core project dependencies or main algorithm copies in migrated facades |

[Package/consumer observations](core-refactor-package-results.json) record included paths,
dependencies, the unchanged consumer hash and deployment conditions. No NuGet publication
or source push was performed.

Public API comparison covers exported types, public/protected members, flags/default
parameters, semantic nullability and code-analysis/obsolete/params contracts. Main has
2,174 entries and Core has 85 entries after Slim→Core mapping; see
[API observations](core-refactor-api-results.json). The unchanged old main consumer
assembly is also exercised against the new libraries. Its deployment includes an
updated dependency manifest: swapping DLLs while retaining an old `.deps.json` does
not declare the newly required Core assembly.

The regression suite covers product policy differences, custom low-level models/passes/
measurers/commands/renderers, source indices and unselected geometry, fonts/styles/VS,
merged/hidden/repeated geometry, object bounds, links, range selection, trim and continuous
output. Mixed tests use distinct fonts, alternating Core→main→Core, 50 sequential and
50 concurrent conversions, waiting cancellation and Save failures followed by success.
They verify session/image/native-font release, weak snapshot collection and durable public
finalized runs after a Core reset. Observers confirm common geometry/page/command/border
paths, at most one active streaming page payload and one Save. No product GC.Collect
or PDF import was added.

## Output results

Main: nine original sample workbooks plus shape/link, explicit-range and trim/page-order
cases, 41 PDF pages. Core: nine normalized sample workbooks, 17 PDF pages. All baseline/final
page sizes, text geometry, image bounds, drawings/border operators and annotation targets
match after removing PDF object identity metadata; rendered dimensions match and maximum
pixel MAE is zero. Main PNG/SVG paginated and continuous outputs and Markdown match bytes.
Markdown uses sheet selection without explicit range/trim/page selection, preserving its
existing input restrictions. See [output observations](core-refactor-visual-results.json).

The existing Core reference/golden validator also passed all 17 raster pages without
changing the stored PNG bytes. Its fixed renderer/font provenance remains in the baseline
manifest; current observations are under `TestResults/Core/Validation/report.json`.

## Performance and memory results

Three fresh processes per version/fixture, without concurrent builds/tests. Each product is compared with its own baseline.

| Product / input | Time change | Peak RSS change | Allocation change |
| --- | ---: | ---: | ---: |
| Core / 1000 rows | +5.8% | -0.3% | +0.1% |
| Core / 5000 rows | -0.6% | -0.5% | +0.2% |
| Core / 10000 rows | +0.2% | -0.3% | +0.2% |
| Full / 1000 rows | +8.9% | +0.1% | +21.1% |
| Full / 5000 rows | -1.2% | -6.0% | +20.2% |
| Full / 10000 rows | -6.1% | +1.3% | +20.5% |
| Core / 100 repeated images | -3.0% | -0.1% | +0.1% |
| Full / 100 repeated images | +8.3% | -0.4% | +0.7% |
| Full / shapes, transformed objects and links | +6.7% | -1.6% | +14.6% |
| Full / font decorations and runs | +8.5% | +0.8% | +1.0% |

All ten cases meet the ≤10% time/peak RSS target. Main allocation remains higher because
public compatibility commands and neutral commands coexist within a page; it is recorded,
rather than claimed to improve. Style/finalized-layout caches reduced that increase.
Streaming observers report one active page payload, one Save and zero imports; native
font creation/disposal counts match. No adapter copies image/font bytes or rematerializes
all cells per pass. Core has no font discovery. Managed peaks and individual trials are
in [raw observations](core-refactor-measurements.json).

The main 1,000-row initial final-only pass showed +19.5% against earlier baseline trials.
Its ClosedXML load also increased (median 543→685 ms), while larger cases improved.
A paired alternating baseline/final recheck gave +8.9%; both earlier and recheck samples
and the reason are retained in the JSON. This is environment-specific evidence, not a
performance guarantee for arbitrary workbooks.

## Reproduction

```bash
dotnet restore ExcelRenderer.slnx
dotnet restore ExcelRenderer.slnx --locked-mode
dotnet build ExcelRenderer.slnx -t:Rebuild -c Release --no-restore --verbosity:minimal -m:1
dotnet test ExcelRenderer.slnx -c Release --no-build -m:1
dotnet build samples/ExcelRenderer.Core.Sample/ExcelRenderer.Core.Sample.csproj -c Release -m:1
dotnet pack src/ExcelRenderer/ExcelRenderer.csproj -c Release --no-restore -o artifacts/packages -m:1
dotnet pack src/ExcelRenderer.Fonts/ExcelRenderer.Fonts.csproj -c Release --no-restore -o artifacts/packages -m:1
dotnet pack src/ExcelRenderer.Tool/ExcelRenderer.Tool.csproj -c Release --no-restore -o artifacts/packages -m:1
python3 scripts/check-core-dependencies.py
python3 scripts/compare-core-api.py --baseline-worktree ../ExcelRenderer-baseline
source scripts/test-fonts.sh
python3 scripts/check-core-shared-mutations.py
python3 scripts/check-core-mutations.py
python3 scripts/check-output-mutations.py
```

Build the current Core.Validation/Performance and baseline Slim.Validation/Performance
harnesses in Release before performance comparisons. The baseline Slim harness received
only allocation instrumentation: capture `GC.GetTotalAllocatedBytes(true)` before each
render and report its end-minus-start value as `AllocatedBytes`; baseline product source
was unchanged. Run with no concurrent builds/tests:

```bash
python3 scripts/compare-core-performance.py --baseline-worktree ../ExcelRenderer-baseline
python3 scripts/compare-core-output.py --baseline-worktree ../ExcelRenderer-baseline
```

The output comparison additionally requires PyMuPDF, pypdf, Pillow and pdftoppm, plus
built baseline/final Tool and Slim/Core.Validation harnesses. Generated
performance fixtures include 1,000/5,000/10,000 rows, 100 repeated images, shape/link
geometry and finalized font decorations. Raw samples retain all three fresh-process
trials per product/version. Compare median wall time and peak RSS against each product's
own baseline; ClosedXML and PDFsharp document retention remain, so constant memory is
not promised. Source-free package consumers use a separate temp directory/cache and
only a PackageReference, rather than source ProjectReferences.

Windows execution was not available locally. The existing Core Ubuntu/Windows CI matrix
and existing main/Tool CI coverage are retained; Linux results are not Windows results.
The renamed 17 baseline PNG files retain their original bytes and were not regenerated
to make comparisons pass. Historical pre-refactor measurements remain clearly labelled
in [core-validation.md](core-validation.md).
