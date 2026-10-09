# Two-engine architecture

`ExcelRenderer.Core` is the renamed single-font PDF engine and the shared foundation.
`ExcelRenderer` extends that engine and retains its original public assembly/types.
Core is `netstandard2.1`, has no ProjectReference, and remains nonpackable. The existing
ExcelRenderer and Tool packages ship the Core DLL from the same build.

```text
ExcelRenderer public APIs / multi-format orchestration
    FullExcelReader → CoreExcelReader hooks
    FullLayoutPolicy → Core geometry / sheet plan / page builder
    CoreTextLayoutAdapter → finalized full runs in borrowed typed payloads
    FullDrawingExtension → common cell layers + image/shape Z stage
    FullPdfRenderer → Core append / basic paint / image decode / Save
    PNG / SVG / Markdown / continuous / trim / link preflight remain in main
                         ↓
ExcelRenderer.Core (also independently usable through ProjectReference)
    input/spool → basic reader → geometry/pagination/placement → commands → PDF
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

A single Core PDFsharp gate owns global font setup through measurement, drawing, Save,
resource disposal and resolver cleanup. Conversion paths borrow their session; independent
public measurers/renderers acquire their own. Both engines can alternate or run concurrently
with the same Core DLL in one load context. Unrelated external PDFsharp users and separate
AssemblyLoadContexts do not share the gate. Public resolved font owners retain immutable
snapshots across resets; expired snapshots have weak process registrations.

See [final source ownership](core-source-map.md), [migration instructions](core.md), and
[executed verification](core-refactor-validation.md). Windows execution is left to the
retained Ubuntu/Windows CI matrix; local Linux results are not presented as Windows results.
