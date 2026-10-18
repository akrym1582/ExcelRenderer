# Main and Core architecture

`ExcelRenderer.Core` is the single-font PDF engine and the shared foundation.
`ExcelRenderer` extends that engine and retains its original public assembly/types.
Both use the same Skia drawing and PDF foundation with different product policies;
see [Skia rendering and font design](skia-rendering.md).
Core is `netstandard2.1`, references only the shared `ExcelRenderer.OpenXml` project, and remains nonpackable. Generic workbook input validation/spooling belongs to OpenXml; Core keeps compatibility adapters. The existing
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
    OpenXml input/spool → selected reader → geometry/pagination/placement → commands → PDF
    regular single font; no shapes/annotations/selection or image splitting
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
