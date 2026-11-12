# Rendering review fixes

Baseline: `df74eb0523e712db96e918c3038abb077590365a` (1.10.5).
The separate historical comparison starts at `a71e21d9f8a612c8747918383eced1742b385ada`,
before the main rendering optimization. [日本語](rendering-review-fixes.ja.md).

## Changes

1. **CLOCK insertion.** Replacement and eviction remember their released slot.
   Insertion reuses it rather than advancing and searching the ring again. Entry
   count, payload budgets, second chances and hot-key behavior remain bounded.
   `Scans` now counts free-slot searches as well as eviction scans; old and new
   scan metrics therefore have different coverage. Tests bound total scans for
   unique inserts at capacities 8/512, check retained values and exercise
   replacement plus multiple evictions required by a payload budget.
2. **One page-build purpose.** `PageBuildPurpose` replaces the independent
   collection/preflight booleans. The plan validates and resolves Render,
   FontCollection or Preflight before measurement. Ineligible product preflight
   becomes Render. The same resolved value reaches placement and commands,
   including empty pages. Core owns cell → product-object → header/footer text
   collection order; `ICoreDrawingExtension` supplies text-bearing product
   objects. Main no longer reconstructs that sequence. Trim/range/diagnostic/
   observer request eligibility and CF/shape-only fallback are preserved.
3. **Cache roles.** Default measurement and run completion share one built-in
   measurer. A caller-supplied measurer keeps a separate built-in completion
   service. Ordinary unwrapped text uses the whole-layout cache and skips
   duplicate line-cache lookup/admission/accounting. Wrapped and rich fragments
   retain line reuse; compatibility/rich candidate widths retain their width
   cache. Rich layouts retain decoration-aware keys and standalone weak values.
   No cache limit, native-resource lifetime or public API is widened.

The diagnostic wrap fixture had 71,984 line-cache hits. Removing that cache
wholesale would discard demonstrated reuse. Ordinary text instead had 2,000
line-cache misses and zero line hits alongside 2,000 whole-layout hits; this is
the redundant path removed here. Tests cover that distinction, registration
invalidation, exact layout/glyph/source clusters, CF fallback, source regions,
shape Z order and headers/footers.

## Measurement protocol and reproduction

The saved Release runtimes are under `artifacts/rendering-review/`. No builds,
tests or competing renders overlap measurements. Processes alternate before/
after order across three rounds. The clock-only, cache-role and historical
comparisons use three warm-ups and five measured samples per process. The broad
22-case comparison uses the existing two-warm-up/three-sample protocol, with
cold observations retained separately. Observer-enabled captures are separate.
Peak RSS is a process high-water mark, not a per-iteration reset or a memory
limit. These protocols are reported separately, without pooled-median acceptance.

[Source state and patches](validation/rendering-review-fixes/source-state.json)
identify the inputs to each saved runtime: base, base + clock-only patch, then
preflight/service sharing, then cache-role separation. Apply the patches in that
order to the baseline and build the Performance project in Release, saving the
complete output directory at each stage. The historical runtime is built from
the historical commit independently. Input bytes come from the existing Core
performance, sample and retained-workbook fixtures; the generated range source
and its hash are retained. Paths, DLL/font/input hashes, raw samples and commands
are recorded in the [evidence directory](validation/rendering-review-fixes/).

```sh
mkdir -p artifacts/rendering-review/inputs
cp docs/validation/rendering-review-fixes/range-source.xlsx artifacts/rendering-review/inputs/
python3 scripts/measure-main-optimization.py \
  --before artifacts/rendering-review/before \
  --after artifacts/rendering-review/after \
  --output artifacts/rendering-review --prepare
python3 scripts/measure-main-optimization.py \
  --before artifacts/rendering-review/before \
  --after artifacts/rendering-review/after \
  --output artifacts/rendering-review --rounds 3
python3 scripts/recheck-main-optimization.py artifacts/rendering-review --rounds 3
python3 scripts/measure-main-optimization.py \
  --before artifacts/rendering-review/before \
  --after artifacts/rendering-review/after \
  --output artifacts/rendering-review --diagnostics
```

The focused comparisons use `scripts/benchmark-render-finalization.py` with the
same `cases.json`, `--warmups 3 --samples 5 --rounds 3`, and the appropriate
runtime pair. Raw records retain those exact commands. The controlled follow-up
rechecks every broad-comparison case exceeding 5% in time or RSS with tiering
disabled, five warm-ups and eight measured samples. It investigates the primary
results rather than replacing them. Historical validation reports remain intact.

## Results and verification

The retained results below distinguish clock-only changes, cache-role changes,
the final broad comparison, and the historical baseline. This work fixes the
extra ring scan and duplicate admission directly; it does not promise a general
speedup or that every time/RSS ratio stays within 5%. Windows was not run locally.

| Comparison / ordinary text1000 PDF | Time change, rounds 0 / 1 / 2 | Allocation change |
| --- | --- | --- |
| CLOCK only vs 1.10.5 | -16.5% / -30.8% / -22.7% | -2.1% |
| Cache roles vs CLOCK + preflight/shared service | +3.4% / +12.1% / -22.2% | -0.6% |
| Final vs historical pre-main-optimization | -6.3% / -14.0% / -22.5% | -5.5% |
| Final vs 1.10.5, primary | +34.7% / -16.6% / -6.8% | -0.8% |
| Final vs 1.10.5, controlled | -5.5% / -6.1% / -13.3% | -0.6% |

Allocation changes in the focused/primary rows are descriptive pooled medians;
controlled allocation is stable across its rounds (approximately -0.6%). Time
acceptance is not derived from pooling. The role-only comparison varies from
+12.1% to -22.2% in time; it supports removal of duplicate admission/accounting,
not an isolated general speedup claim. The clock-only and historical comparisons
are separate process runs with the same saved bytes and the stated protocol.

All three controlled ordinary-text rounds improve (5.5%, 6.1%, 13.3%), while the
primary round 0 regresses by 34.7%. Both observations are retained. Range/trim
primary changes are +7.2%, +39.4%, +10.0%; controlled changes are +1.3%, +1.7%,
+1.4%. Rich PDF controlled changes are +4.5%, +2.3%, -1.1%. Other time/RSS flags
remain in the full per-round analysis, including cumulative peak RSS flags for
ordinary text. Their cause is not established as noise alone. This is not a
claim that every 5% target passes. The original ordinary-path cost is directly
removed, and the normal-workbook controlled/historical results support the fix.

The nine layout comparisons have identical hashes of line geometry, text,
resolved runs, selected glyphs, UTF-8 source and clusters. Main PDF comparison
covers 12 cases / 108 pages with identical 96-DPI pixels, text/glyph geometry,
drawings, images, links, page sizes and embedded font subset hashes. All 12
PNG/SVG artifacts match after canonicalizing SVG resource IDs. Every observed
final conversion has maximum one page payload, and native paint/typeface/PDF
font/subset creation and disposal counts balance.

| Required check | Result |
| --- | --- |
| Debug and Release solution Rebuild | 0 warnings / 0 errors in both configurations |
| Complete Release solution tests | 3,087 passed, 0 failed, 0 skipped (Core 470, main 1,196, Tool 135, Formulas 833, Mapping 226, OpenXml 227) |
| Core coverage gate | 462 passed; line 84.90%, branch 74.36%, thresholds 82.12% / 70.08% |
| Core Performance category | 8 passed |
| Public signature audit | Main 1,894 / Core 68 signatures; no additions or removals |
| Measured/rebuilt assembly identity | All measured final product DLL hashes match the Release rebuild |
| Source namespace and ownership checks | Passed; 743 declarations plus two compiler shims |
| Forbidden dependency audit | Passed; 24 restore graphs and 726 managed AssemblyRef inventories |
| Reader/Core isolation | Independent restore/build/publish, graph, AssemblyRef and separate-process execution passed |

No package layout, project file or dependency version changes were made. Validation
logs and a hash manifest are in the evidence directory. Source hashes still match
the measured runtime's recorded source state after all checks.
