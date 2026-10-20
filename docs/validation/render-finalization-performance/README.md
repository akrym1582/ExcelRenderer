# Render finalization and performance validation

This report covers tasks A–E in the supplied finalization/performance instructions.
The measured product baseline is `38390a69490fa56ca2ff28006d573eff5a3d925c`, after the
border fix and main `513ddd4d424d9afa6e92ed154abdc631841ba302`. The stacked PR base was
updated to `64f5a7781dac317d9b4c1ff4794262d1604bd823` (preceding border PR #99's CI
fixture/assertion/review follow-ups); its `src`, `tools` and `build` trees equal the
product baseline. No unrelated unmerged feature branch was imported.

See [the implemented contracts](../../render-finalization-performance.md) and
[日本語](../../render-finalization-performance.ja.md). Historical reports and frozen
PDFs remain evidence for their own revisions; this report adds same-format comparisons.
The reviewed Core border-edge update comes from the preceding PR, not a relaxation for
this performance change.

| Task | Implementation | Verification |
| --- | --- | --- |
| A | Existing Performance tool `render-profile`; reused CF Phase 2 fixture generator; saved before/after Release runtimes; separate diagnostic processes | Raw rounds, cold/warm samples, allocation/GC/peak RSS/output sizes, DLL/input/font hashes and inclusive phase/counter observations retained below |
| B | Common finalizer, preserved custom geometry/shape TextCommand, missing run/glyph completion, idempotent branch/page identity, native PNG/SVG metrics | Direct renderer, nested shape, partial-run, post-scope replay and normal-path legacy-paint counters; full existing rich/IVS/fallback/header tests |
| C | Core direct glyph registration; combined main preflight/collection; two builds per selected PDF/PS page | Four-format page-count/payload tests; transparent nested collection without image decode; PS error opens no sink; timestamp/Abort/cancel/spool tests |
| D | Finite candidate union, adjacent Mapping geometry coalescing, band-shared rule arrays, lazy maps/targets and disabled preparation | Duplicate budget, overlapping rules, reversed source anchor, unique-area fallback; existing CF population/priority/merged/selection tests and actual before/after counters |
| E | Selected-glyph ink cache; weak alpha metadata; shared main shape geometry; borrowed immutable glyph/source/cluster spans | Alpha classes after eviction, identity and cancellation; trim underline expectation; same-format independent raster/text/font checks; existing PDF/Core Unicode extraction tests |

## Protocol

No builds, tests or render jobs overlap the paired measurements. The pilot used two
warmups; continuing latency changes within its measured samples led to the retained
final protocol with eight warmups. Both raw series are retained, with their own DLL
hashes. The final series measures source `ba4c377` and includes the later limit-diagnostic,
streamed-title, allocation-free completeness-check and nested-resolver safeguards. Runs use identical
fixture bytes, font options, .NET runtime, driver and settings. The timed baseline's
product DLLs were saved before implementation and never instrumented. Large runtimes
stay in workspace artifacts rather than temporary storage. Each fresh process records
a separate cold conversion, warmups, then measured samples; baseline/after order
alternates by round. Warm timings have no observer. Peak RSS is the process lifetime
high-water mark, including startup/warmups, not a sampled conversion delta.

Diagnostic processes use two separate warmups, an unobserved control and one observed
conversion with the same file sink. Artifact counts and hashes must match between control and observation. SVG uses
the existing ReaderSnapshot ID/reference normalization because Skia assigns process-global
clip IDs; other artifact bytes and sizes must match exactly. Raw SVG sizes are retained. Hashing happens after the timed/allocation interval. Inclusive phase times overlap and must not be summed. Actual event counts
come from the work itself, not extra model/command traversal. The isolated baseline
counter build adds only observations; its source patch and hashes are retained. Its
wall time is excluded from performance results. The original uninstrumented baseline
still supplies all before timings/allocations/GC/RSS/size values.

The render grid's native dimensions and explicit row breaks produce real 1/10/100-page
cases. The matrix also covers borders, repeated/unique strings, rich text, shape text,
IVS and missing PUA with retained Unicode, repeated large opaque images, all four
formats, trim/range/page selection and continuous PNG/SVG. CF inputs reuse Phase 2
plain/Phase 1/shared population fixtures at 10k/100k, disabled/selected, overlap, Mapping
and recalculation. Unsupported PS color fonts/partial alpha are regression failures,
not mixed into normal performance cases. No-CF/disabled/Core snapshot/population
contracts are checked by existing tests and isolation rather than a new Core feature.

## Retained results

See [the full 36-case table and timing interpretation](measurements.md). The final
warm series uses eight warmups and retains nine measured samples per stage/case;
the bounded small-case follow-up uses 50 warmups and 15 measured samples. Latency
for the noisy small cases is explicitly inconclusive, including the +11.6% PNG value
at eight warmups. Allocation and work-count reductions are reported independently.

The final solution rebuilt in Debug and Release with zero warnings/errors and passed
all **2,634 tests** (Core 326, Formulas 833, Mapping 172, OpenXml 213, main 972, Tool 118).
Namespace, Core/Formulas ownership/dependency checks, removed-dependency inventories
and standalone Reader/Core restore/build/publish/execution passed. Main's 1,883 and
Core's 83 public API entries have no additions/removals. Product DLL hashes from the
final rebuild equal the saved timed-after runtime. The later diagnostic-driver-only
change normalizes Skia's process-global SVG resource IDs exactly as the existing
ReaderSnapshot comparison does; its own Debug/Release rebuilds also have zero warnings.
It changes no product DLL or primary timing data. Both driver identities are retained.

The four originally named `shapes-*` cases in the primary matrix use the retained
**seed** from `ShapeAndFontTests.CreateWorkbookWithShapes`; the test adds DrawingML
after opening that seed. The seed alone has no drawing part and is not evidence for
shape text. The separate `shape-text-*` evidence adds the same standard DrawingML
structure with five supported shapes (rectangle, ellipse, rounded rectangle and both
callouts), rotations and Japanese text, then repeats the paired four-format protocol,
observer checks and independent raster/text comparison. The generator now completes
this seed automatically. Original primary raw names/hashes are preserved for honesty.

Final quality gates: **47 same-format artifacts** (43 primary plus four actual
shape-text artifacts) have zero changed pixels at their tested resolutions. PDF checks
compare page dimensions, glyph origins/IDs, Unicode through MuPDF and Poppler, and
links; PS checks execute Ghostscript and validate Type 42 checksums, metadata and
show-operator/GID correspondence. All observer/control checks passed (72 primary,
eight shape-text stage/cases, plus independent counter runs), using the existing SVG
resource-ID normalization only. Frozen main PDF gates passed 25 inputs (27 existing
reviewed differences); frozen stage B passed with no historical AA exceptions; all
three Core inputs passed. Missing border/text, extra ink, IVS/font-metrics/font-bounds
mutations remain rejected. Frozen historical PDFs and thresholds were not changed.

[Actual work counts](counters.md) show PDF build events 300→200 for 100 pages,
PS 30→20 for 10 pages, null collection surfaces 100→0, shared CF visits
400,000→100,000 with 100,000 unique candidates, application-rule lists
100,000→1, Mapping lists 10,000→1, alpha scans 20→1 (19 cache hits), and actual
shape-text PNG compatibility paints 5→0. Source/cluster and public API checks remain.

Artifacts include per-stage hashes, raw measured samples, environment records,
independent diagnostic/control rows, counter-only source patch, font/input bytes and
hashes, build/test/audit logs and compressed TRX/profiles/comparison details.
`source-hashes.json` records product/test/tool sources; prohibition-script filenames
are omitted from that new inventory to respect the existing forbidden-reference gate.
`products.json` distinguishes the timed and diagnostic driver hashes; product DLLs
are identical between those runs. `artifact-hashes.json` binds the retained evidence.

## Reproduction

Save the baseline runtime before changing product source. Build the current Performance
tool and copy only its DLL/PDB into that saved baseline directory; retain the original
product DLL hashes. Save the after runtime separately after the final Release build.
The existing generator and CF fixture helpers create the inputs once:

```bash
source scripts/test-fonts.sh
python3 scripts/benchmark-render-finalization.py \
  --before artifacts/render-finalization-performance/baseline-runtime \
  --after artifacts/render-finalization-performance/after-runtime \
  --output artifacts/render-finalization-performance/matrix --generate
# Reuse these exact inputs for every round and follow-up; do not regenerate them.
taskset -c 0 python3 scripts/benchmark-render-finalization.py \
  --before artifacts/render-finalization-performance/baseline-runtime \
  --after artifacts/render-finalization-performance/after-runtime \
  --output artifacts/render-finalization-performance/matrix \
  --rounds 3 --warmups 8 --samples 3
# Diagnostic work is a separate process set with warm control samples.
python3 scripts/benchmark-render-finalization.py \
  --before artifacts/render-finalization-performance/baseline-runtime \
  --after artifacts/render-finalization-performance/after-runtime \
  --output artifacts/render-finalization-performance/matrix --diagnostics
python3 scripts/compare-render-finalization.py artifacts/render-finalization-performance/matrix
```

The retained `cases.json` uses repository-relative paths into the retained `inputs/`
directory. To reuse the exact evidence bytes in a new measurement directory, copy that
specification and link its input directory rather than regenerating the XLSX files.
The final specification also records the effective eight warmups for every case.

Use a CPU allowed by `os.sched_getaffinity(0)` on another machine. For diagnostic-only
baseline counters, create a separate checkout of `38390a6`, run
`scripts/instrument-finalization-baseline.py <checkout>`, build its Performance tool,
and use that runtime only with `--diagnostics`, into a separate output folder with
copied `cases.json`/input bytes. Never substitute it into the timed baseline.

The solution verification commands are the attached plan's restore, Debug/Release
Rebuild, font-isolated full Release tests, namespace/Core/Formulas/removed-dependency
checks and `validate-reader-isolation.py`, run sequentially. Frozen PDF/Core gates and
mutation checks remain in force. No product project/package/distribution files changed,
so no new package distribution was introduced.

## Limits

The measurements characterize the retained synthetic and regression inputs under this
container's scheduling. They are not a universal 5% latency guarantee, Excel UI/print
conformance or a claim about the previously noisy small no-CF timing. Microsoft Excel
was unavailable. Weak alpha/ink caches are bounded and may rescan/recompute after their
capacity is reached. Skia native ellipses and PS cubics deliberately preserve the existing
format-specific approximation. Observer control differences are diagnostic samples,
not extra warm performance results. Existing unsupported-format/font policies remain.
