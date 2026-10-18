# PDFsharp removal migration (unreleased breaking change)

PDFsharp is removed from main, Core, tests and distribution graphs. PDF and default
PDF/PNG/SVG measurement use SkiaSharp. This is a breaking change, not a compatible
wrapper around the removed APIs. Package versions and publication are separate release work.

## Public API changes

| Removed API | Replacement |
| --- | --- |
| `ExcelRenderer.PdfSharp.PdfSharpTextMeasurer` | `ExcelRenderer.SkiaSharp.SkiaTextMeasurer` implementing the same three measurement/layout interfaces |
| `ExcelRenderer.PdfSharp.PdfSharpRenderer` | `ExcelRenderer.Pdf.PdfRenderer` implementing `IRenderer` |
| `ExcelRenderer.PdfSharp.PdfSharpFontResolver` and its constructors / `GetFont` / `ResolveTypeface` | `FontOptions.FontFiles` / `Registrations`, or an `IFontManager` supplied to the new measurer/renderer |
| Resolver registration, `GlobalFontSettings`, reset and font-session assumptions | Conversion-owned immutable physical font snapshots; no global setup |

The metadata API diff in [validation results](pdfsharp-removal-validation.md) lists
the exact removed/added public signatures. Core's high-level `ConvertAsync`, main
`ConvertToPdfAsync` / `RenderAsync`, `ITextMeasurer`, `ITextLayoutService` and
`IRichTextLayoutService` retain their signatures. Caller-provided services/streams
remain caller-owned. Public finalized layouts retain internal immutable font bytes, selected glyph IDs
and original UTF-8 clusters without disposed native handles. Caller-visible `ResolvedFont.FontData` stays
caller-owned and never exposes the internal snapshot buffer.

```csharp
using ExcelRenderer.Fonts;
using ExcelRenderer.Layout;
using ExcelRenderer.Pdf;
using ExcelRenderer.SkiaSharp;

var fonts = new FontManager(new FontOptions
{
    AllowSystemFonts = false,
    UseFontPack = false,
    FontFiles = ["/app/fonts/report.ttf"],
});
var measurer = new SkiaTextMeasurer(fonts);
var layout = new ReportLayoutEngine(measurer).Layout(sheet);
var commands = new ExcelRenderer.Drawing.DrawCommandGeneratorPass().Generate(layout);
new PdfRenderer(fonts).Render(commands, sheet.PageSettings, output);
```

For high-level calls, pass `FontOptions` through the existing export/request options.
The optional Fonts package remains optional. Core still accepts only one supplied
regular static TrueType font, removes variation selectors to their base characters,
and does not gain links, fallback, shapes or range selection.

## Backend option and serialized values

| Value | Behavior |
| --- | --- |
| Option omitted / new options instance | `SkiaSharp = 1` |
| Explicit `SkiaSharp = 1` | Supported; ignored for non-PDF API formats |
| Obsolete reserved `PdfSharp = 0`, including `default(PdfBackend)` | `NotSupportedException` before input/output access; explicitly migrate stored requests to 1 |
| Other enum number | `ArgumentException` before input/output access |
| JSON property omitted | Property initializer selects 1 |
| JSON number 0 / 1 | Keeps its exact number; validation rejects 0 and accepts 1 |
| JSON `null` for the enum property | `JsonException`; the property is not nullable |

A string enum converter still maps the obsolete name to reserved zero; it does not
make that engine available. Do not renumber enum values or silently reinterpret old data.

CLI defaults to SkiaSharp. `--pdf-backend skiasharp` remains accepted by `pdf` and
`render --format pdf`, including `--data`. `pdfsharp`, unknown names and uppercase
variants are parse errors. Explicit backend options on PNG/SVG/Markdown remain parse errors.

## Runtime and ownership

- Main/Core PDF require the pinned HarfBuzz subset adapter and SkiaSharp native assets.
  Source builds run `python scripts/build-harfbuzz-subset.py` before build/pack.
  Published applications need no Python, PDF reader, Ghostscript, Node or server.
- Missing subset assets fail with a concrete diagnostic and abort the artifact. They
  do not select another engine or embed complete outline fonts. PNG/SVG/Markdown
  do not invoke the PDF subset adapter.
- Independent public `SkiaTextMeasurer` calls own/restore native scopes and serialize
  access to their own manager/cache. Conversion-local instances and a supplied Skia service invoked by its conversion borrow that scope. Independent PdfRenderer calls always own/restore a separate session, including calls that inherit another session through ExecutionContext.
  There is no process-wide conversion lock. Conversion-local caches are limited to
  512 entries / 2048 characters per layout, prefix-width and finalized-line cache. Keys include style and registration generation; width/line caches apply only to the built-in manager inside a conversion. Standalone rich calls with the built-in manager
  keep at most eight weak layout references, so the service does not retain font
  snapshots after callers release them. Custom managers without registration
  generations resolve afresh on each independent call.
- Point layout uses scalable glyph advances and explicit unhinted linear metrics.
  Core's rounded 96-dpi maximum digit width remains separate. PNG/SVG retain their
  established native raster advance policy and consume the stored glyph IDs.
- Main font-format probes cover static TTF, CFF, default variable outlines, TTC face
  zero and COLRv0/v1. This does not promise collection face selection, arbitrary
  variation coordinates or newly complete shaping/RTL support. Core's restrictions
  are unchanged.

The internal generated-PDF finalizer accepts pinned Skia classic-xref output only.
It appends bounded corrections for 0.001pt boxes, RGB transparency groups, resolved
links/tooltips and image interpolation. It is not a public/general PDF editor;
xref/object streams, encrypted and signed input are rejected. Original native bytes
and encoded content/font/image streams are retained. Incremental color-image revisions
repeat encoded image bytes, increasing some image-heavy files.

Retired instrumentation: `xFontCreated`, font-gate wait/reset/registration metrics.
New `pdfFontCreated/Disposed` and `pdfSubsetTypefaceCreated/Disposed` count their
actual Skia resources; old names do not acquire new meanings.

See [the inventory](pdfsharp-removal-inventory.md) and
[executed checks, measured limits and unexecuted platform/viewer work](pdfsharp-removal-validation.md).
