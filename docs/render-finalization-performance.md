# Command finalization and render performance

The implementation preserves the border-resolution branch (`38390a6`) and the newer main
already incorporated there (`513ddd4`). The original investigation at `2996f6c` predates
those changes. This work uses the current metrics, shared borders, rich text and PS
font behavior as its baseline. [日本語](render-finalization-performance.ja.md).

Every main-library rendering format now enters `CommandTextFinalizer` before command
diagnostics, trim bounds, font collection or painting. Supplied layouts retain line
breaks, size, baselines, positions and rich styles; only missing runs/glyphs are completed.
An existing shape `TextCommand` is retained. Complete branches and page lists retain
object identity. Nested viewports recurse, while continuous output stays an iterator.

PNG/SVG compatibility input uses `SkiaCompatibilityTextLayout`: it freezes the existing
native metrics, shrink measurement and float positioning. It does not adopt PDF's
unhinted metrics. This is necessary to preserve existing PNG/SVG pixels. The layout
contains immutable font snapshots and glyph selections, with no native handles.
Independent public renderer calls establish and restore their font/image resource scopes.
A nested raster call reuses the session layout service only for the identical resolver
instance, preserving a public caller's explicit font policy.

PDF/PS preflight builds and finalizes each selected page, checks diagnostics and bounds,
fixes its descriptor, then collects that page's font usage. It releases commands before
proceeding. Drawing rebuilds the page once: **two builds per selected page**, including
trim/range and page selection. Links resolve from metadata between passes. The existing
conversion clock snapshot is shared by both passes. Early-created writers are disposed
on preflight failure or cancellation; newly detected PS preflight errors open no sink.
Font-preparation/write errors retain the existing output Abort contract.

PDF collects glyphs directly into Core's `PdfFontCollection`, including transparent text
under the established collection policy. It creates no null surface, placement, paint,
image decode or shape path. PS preserves its full format validation, including image
transparency and font capabilities, while avoiding text placement during collection.
Glyph/source/cluster arrays remain privately owned; internal read-only spans are borrowed
by native APIs that copy them. Per-draw positions and native text buffers still need
owned storage and are not shared as writable arrays.

Conditional formatting separates display candidates from population statistics. It
coalesces adjacent source geometry without modifying source sqrefs or formula anchors,
streams finite body/title intersections under the same work budget (without materializing
the title-row × title-column product), unions row bands and column intervals, then visits
each display coordinate once. Ordered rule arrays are shared by row/column bands; cell
results still evaluate independently. Source priority, stopIfTrue, unsupported-property
reservation, border neighbors and merged perimeter behavior are preserved. Composition
maps are created only for matching explicit properties; dynamic-fill-only application
uses an immutable empty view. Formula target dictionaries are created only for
Recalculate. Supplied target delegates avoid unused hidden/merge indices; disabled CF
skips formula/style preparation and display indices. No-CF/disabled/Core snapshot
contracts remain unchanged.

The display union retains the 2,000,000 unique-coordinate budget, with separate finite
16,000,000-unit metadata/normalization work limits and 2,000,000 retained rectangle/rule
reference limits. All candidate/rule plans are prepared before applying styles, so a
normalization limit keeps ordinary formatting for the whole sheet. A bounded diagnostic
probe (at most 2,000,000 coordinates, with row cancellation) finds a visible target
when the first source coordinate is hidden; it does not allocate candidate styles. Sorting receives
bounded inputs; these work units are range/event/reference inspections, not CPU
instructions. Existing population limits and whole-source statistics remain separate.

Trim uses finalized glyph ink and positions. A conversion-local identity cache retains
at most 512 small selections (at most 2,048 glyphs and 16,384 source bytes each), checks
before face acquisition and holds no native handles. Rotation, clip, explicit IVS paths,
emoji fallback and underline remain part of the outer bounds calculation. One established
inconsistency is fixed: ordinary-font underlines on completed runs now extend trim bounds
to the actual underline, just as rich underlines do; an independent baseline-coordinate
test covers this. Frozen output contracts are unchanged.

Core's image-resource owner caches PS alpha classification under weak byte-array identity,
up to 512 entries, independently of decoded-image eviction. It checks cancellation per
row, skips scans for opaque metadata and retains no extra bitmap. Clearing a full metadata
cache can cause a later rescan; it never grows without bound. Main's `ShapeGeometry`
shares rectangle/ellipse/rounded/callout definitions. Skia retains native oval/round-rect
primitives; PS retains its established cubic approximation. Paths are scoped and disposed.
Core still exposes no PS API, shapes, conditional formatting or new format options.

The [validation report](validation/render-finalization-performance/README.md) records
saved-runtime hashes, separate diagnostic observations, paired uninstrumented measurements,
cold/warm samples, quality gates, limits and reproduction commands. Observer timings are
inclusive and may overlap (for example finalization inside preflight); they must not be
summed as independent wall time. Build counters count completed build events, including
fully consumed streaming builds. No package/version/distribution configuration changes.
