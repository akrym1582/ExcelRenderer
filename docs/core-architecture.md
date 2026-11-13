# Main and Core architecture

`ExcelRenderer.Core` is the explicit-font PDF engine and the shared foundation.
`ExcelRenderer` extends that engine and retains its original public assembly/types.
Both use the same Skia drawing and PDF foundation with different product policies;
see [Skia rendering and font design](skia-rendering.md).
Core is `netstandard2.1`, references only `ExcelRenderer.OpenXml.Reader`, and remains nonpackable. Generic workbook input validation/spooling belongs to Reader. Core uses Reader input settings and preparation directly with a conversion-local spill callback; main retains its public input settings adapter. The existing
ExcelRenderer and Tool packages ship the Core DLL from the same build.

```text
ExcelRenderer public APIs / multi-format orchestration
    FullExcelReader → CoreExcelReader hooks
    FullLayoutPolicy → Core geometry / sheet plan / page builder
    CoreTextLayoutAdapter → finalized full runs in borrowed typed payloads
    FullDrawingExtension → common cell layers + image/shape Z stage
    PdfRenderer / SkiaPdfDocumentWriter → Core document / subsets / structural save
    PNG / SVG / Markdown / continuous / trim / link preflight remain in main
                         ↓
ExcelRenderer.Core (also independently usable through ProjectReference)
    Reader input/spool → saved-value reader → geometry/pagination/placement → commands → PDF
    explicit supplied fonts, selected per cell; no shapes/annotations/selection or image splitting
```

Extension contracts are internal and visible only to the main engine and existing tests.
There is no DI container, plugin lookup, feature registry, third shared engine, public
extension API, type forwarding, or public record inheritance. Typed payloads never own
native resources. Main font/shapes/link types do not appear in Core signatures.

Normal conversion reads neutral cells and passes them directly to Core layout and command
generation. Selection, diagnostics and Markdown use ordered public dictionary views of
those cells. Range filtering retains neutral values. Caller-built public sheets enter
through a lazy projection with a style cache. Public `ExcelReader.Read` alone materializes
public dictionaries for its compatibility contract. Public pass facades synchronize only
the fields owned by that pass; they do not execute an entire replacement engine.

Each streamed page holds finalized layout/commands only while that page is processed.
Full text runs are preserved with their metrics; adapters do not wrap or measure again.
Image and font bytes are borrowed. Continuous output rescans Core cell layers with a
reused geometry context instead of retaining all commands. Public materializing Layout
APIs retain their existing all-pages behavior.

Native fonts, subsets and images belong to individual conversion scopes. Internal
measurement borrows the current scope; independent public measurement/rendering owns
and restores its scope. There is no global resolver/reset/gate. Finalized runs retain
font bytes, glyph IDs and UTF-8 clusters without native handles, allowing replay after
disposal. Mixed Core/main calls use separate native resources. The public standalone
measurer serializes access to its own manager/cache only; converters have no global lock.

See [source ownership](core-source-map.md), [Core usage and supported scope](core.md), and
[executed verification](core-refactor-validation.md). Windows execution is left to the
retained Ubuntu/Windows CI matrix; local Linux results are not presented as Windows results.

Core's PDF font collection streams the common cell/header/footer text commands from
text-only page payloads. It omits image placement, merged border fragments, border
neighbors and conflict resolution; main preflight retains its complete product policy.
For valid UTF-16 and noncolor outline faces, Core selects paragraph glyphs once and
wraps on Unicode element boundaries with integer hmtx advances. Color fonts and
custom/full measurers retain their compatibility paths. Finalized line clusters are
rebuilt relative to each line. Plan-selected candidates retain source order and
uniqueness, allowing placement to skip a second page-local band index and sort.
The physical-face-keyed cache uses a 512-entry CLOCK, excludes texts over 2048 characters,
and limits retained text/line/glyph units to 131072. Its key excludes decoration
and unused unwrapped width; shrink-to-fit remains outside the cache. PDF positions
are written to the builder-owned span; each Core renderer lazily owns at most two
paints and one dash effect. See [implementation and evidence](core-performance-optimization.md).

## Main rendering optimization follow-up

Main now composes the same renderer-local `SkiaPaintResources`. Core owns the
bounded CLOCK implementation and ordinary single-face `OutlineParagraph` breaker;
main retains font selection, rich decoration and native raster compatibility.
Conditional main PDF text preflight preserves product regions, clips and shape
metadata through the full policy hook, while other preflight requirements retain
their complete path. See [implemented boundaries and validation](main-performance-optimization.md).

Page building carries one resolved `PageBuildPurpose`: rendering, Core font
collection, or product preflight with metadata. The plan validates that purpose
and resolves ineligible product preflight to rendering before measurement.
Placement and command generation consume the same purpose; cell, product-object
and header/footer text collection order belongs to the common command generator.
The drawing extension supplies only its text-bearing objects.

CLOCK replacement/eviction reuses the released slot without searching the ring
again. Scan metrics include free-slot searches. Main's whole-layout cache owns
plain single-line text without wrapping. The line cache also reuses fragments
from explicit line breaks, automatic wrapping and rich text.
Default measurement and run completion share one built-in measurer; a supplied
custom measurer retains a separate built-in completion service. See the
[review fixes and measurements](rendering-review-fixes.md).

## Core glyph storage follow-up

Core outline layout consumes paragraph-relative glyph ranges and freezes each
line once, without an intermediate line glyph array. `GlyphSequence` uses one
Unicode-element pass for identical source/display, while distinct source mapping
keeps its compatibility validation. Public layouts retain immutable data only.
See [implementation and validation](core-glyph-storage-optimization.md).
