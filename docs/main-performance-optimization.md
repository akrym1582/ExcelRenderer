# Main rendering optimization

This follow-up starts at `a71e21d9f8a612c8747918383eced1742b385ada`, after
PR #114 was merged. The original checkout remains unchanged; implementation is in
branch `perf/main-rendering-optimizations`. [日本語](main-performance-optimization.ja.md).

The later [rendering review fixes](rendering-review-fixes.md) correct CLOCK
free-slot scanning, unify page-build purpose and text collection order, and stop
caching plain unwrapped lines twice. The measurements below remain the historical
results for this optimization, not results for that follow-up.

## Implemented boundaries

1. **Paint resources.** Main PDF's `SkiaPdfDocumentWriter` owns its disposable
   `SkiaDrawingContext`; PNG `RenderPage`/`RenderCanvas` and SVG `RenderPage` own a
   context for the drawing call. Each context composes Core `SkiaPaintResources`
   and passes it to shared border primitives and text drawing. Fill and stroke
   paints are lazy, with at most one last-key dash effect. Font/image ownership
   stays in the existing conversion scopes. Independent text drawing owns a
   temporary paint owner. Rich runs set their own colors; every finalized line
   reestablishes the cell color. Raster emoji temporarily use white and restore
   color in `finally`. Shape/image canvas transforms also unwind in `finally`.
   SVG's non-antialiased white page background and native raster compatibility
   measurement retain their existing independent paint policy.
2. **Bounded caches.** Core `Rendering/BoundedClockCache` is the shared eviction
   owner. Main ordinary layout, width, line and rich layout caches each retain at
   most 512 entries and 131072 accounted payload units; inputs over 2048 UTF-16
   units still bypass admission. Width cache units cover key text/family; layout
   units also cover lines, runs, source/display text, UTF-8 bytes and weighted
   glyph/cluster/advance storage. Rich keys include run count and font families,
   including empty runs. These are conservative logical payload units, not a
   byte-accurate heap/RSS limit. Immutable physical font snapshots remain shared
   with the existing conversion/font-manager owners; their storage is not a
   newly bounded font cache. The four independent limits give a finite aggregate
   cache payload limit. Slots are allocated only on admission. Generation changes
   clear old values. Unwrapped available width and ordinary color/underline are
   excluded from geometry keys; family, size, bold/italic and generation remain.
   Rich keys retain complete run fonts and the empty-line font. Public standalone
   ordinary measurement still has no strong layout cache; standalone rich values
   remain weak, at most eight keys and 16384 key units, under the existing instance
   lock. Custom font managers do not acquire an assumed registration contract.
3. **Ordinary outline paragraphs.** Core `Layout/OutlineParagraph` owns UTF-16
   validation, paragraph glyph selection and Unicode-element wrapping for both
   products. Main uses it only with the built-in FontManager and exactly one
   noncolor, ordinary resolved run with identical source/display paragraph text.
   It creates each finalized line from those glyphs and rebuilds source clusters
   relative to the line. Main also passes already measured glyphs to
   `ResolvedGlyphLayout.Finalize` in compatibility/rich line construction.
   FontManager's existing TextRunBuilder now accepts element metadata directly and
   allocates a TextRun only when a physical segment starts; its merge conditions,
   UTF-16 starts and special-glyph flags are unchanged. This avoids temporary
   element runs on paragraph resolution.
   Multiple-font paragraphs, explicit IVS/emoji/missing-glyph transformations,
   invalid UTF-16 and custom managers keep candidate measurement. Rich wrapping
   and PNG/SVG native float wrapping remain unchanged. There is no integer
   accumulation across physical font runs, and no native handle in public layout.
4. **Conditional main PDF preflight.** The converter asks for lightweight
   preflight only without trim, explicit source range, layout/unsupported-feature
   diagnostics or a full-command observer. The full policy additionally requires
   a nonempty text cell among the existing selected candidates and no conditional
   decoration on those candidates. Core retains the exact candidates, measures
   text, skips empty-cell/merged-border/image/border-neighbor payload work, and
   calls `BuildAdditionalPageContent` to retain full source regions, clip policy
   and shape metadata. Collection enumerates shared cell text, shape text in the
   same Z order, then shared headers/footers, before normal finalization/direct
   glyph registration. Other pages and formats use full preflight. Image-only,
   shape-only and empty pages therefore retain their existing path. Selected
   pages still have two builds, with at most one active page payload. Completed
   page layouts/commands are not retained across pages. Read diagnostics and link
   resolution still run at their original pre-output points.

## Existing optimizations, not counted again

Normal pagination already passes `SelectedPageCells` to avoid a second local
index/deduplication/sort. Shared `PdfFontCollection` already writes positions to
its native builder span. Main `Collect` already registers finalized glyphs without
painting, image decoding or a null canvas. `CommandTextFinalizer` already preserves
complete layout and command identity. These are baseline behavior from earlier
changes, not performance improvements introduced here.

## Validation and measurement

See [retained evidence](validation/main-performance-optimization/README.md).
MainPerformanceOptimizationTests compare the old candidate path through an
IFontManager adapter against the new built-in path with a fixed seed and exact
adjacent double wrap boundaries. Comparisons include line geometry, metrics,
physical face, run source/start/advance, glyphs, UTF-8, clusters and advances.
Tests also cover cache boundary counts, decoration separation, large empty-run
keys, generation invalidation, fresh/reused paint pixels and preflight eligibility.
The existing suites retain scope replay, nested/parallel resources, cancellation,
image failures, selected regions, rich text and asynchronous output contracts.

The primary measurement uses saved Release runtimes and existing retained input
bytes. `measure-main-optimization.py` delegates whole conversions to the existing
render-profile harness and adds isolated plain, plain-wrap and rich-wrap batches.
The common harness patch is applied to before and after. Input/font/DLL hashes,
SDK/runtime, CPU/OS, cold/warm samples and alternating-order rounds are retained.
Observers and captures run separately. Inclusive phase durations are not added.
Process peak RSS is a cumulative high-water mark, not a per-iteration reset.

## Deliberately retained compatibility paths and limits

Multiple-font linear wrapping is not enabled: per-run double conversion/addition,
line-specific font resolution and cluster behavior require a separate proof.
Rich candidate wrapping and native PNG/SVG compatibility wrapping are also not
replaced. FontManager's own support/run caches retain their existing policy.
Preflight does not accelerate trim/range/diagnostic/CF-decorated/continuous/PS
paths; image/shape metadata construction remains where needed by product policy.
The first prototype increased allocations for narrow, unique wrapped cells;
retained prototype samples are excluded from the final-runtime comparison. The
follow-up avoids temporary element runs, avoids metric-name allocation when
observers are disabled, and fixes invariant paint states at construction rather
than setting them through native calls at each draw.

Cache admission accounts layout payload, not the complete existing font-resource
memory graph. No public API, TFM, package/dependency version or product boundary
changes are intended.

Historical Core improvement rates and prior validation counts are not main
performance results. A case/round over 1.05 in time or peak RSS is investigated,
not accepted using a pooled median. Linux execution does not establish Windows
results.

## Executed results and acceptance limits

Debug/Release Rebuild completed with zero warnings/errors. All 3061 solution
tests passed. Core coverage passed at line 84.79% / branch 74.40% (443 tests),
and eight Performance tests passed. Main/Core API snapshots (2361/82 entries)
are unchanged. Saved main PDF/PNG/SVG files, seven shared Core cases, the existing
25 PDF fixtures, 19 main/Core parity comparisons and image checks passed.

In the primary narrow long-text workbook, per-round time decreased by
9.7–22.3% and median allocation decreased by approximately 11%. In a separate
20-cell long ordinary-text fixture with source column width 255, per-round time
decreased by 94.9–97.0%, allocation decreased from 3687.76 to 54.34 MiB, and layout
GetGlyphs calls decreased from 120120 to 40. These are fixture-specific combined
implementation results. The additional PDF's pixels, source/glyph coordinates,
drawings, dimensions and embedded subset bytes match.

The all-case time/RSS 5% non-regression criterion is **not met** by recorded
samples. Every flagged primary case received a separate three-round follow-up
with tiering disabled and eight warm samples. Some time/RSS excursions remain;
notably ordinary text1000-Pdf is 8.4–21.2% slower in primary rounds and controlled
time ratios are 1.090, 0.983, 1.080. The short/narrow wrap case has controlled
ratios 1.034, 1.017, 1.037 despite its primary improvement. These observations
are retained, not dismissed as proven noise. Windows was not executed. See the
complete case/round tables and raw evidence before assessing rollout.

Four distribution packages, eleven isolated consumers and font-package split checks also passed. No package publication was performed.
