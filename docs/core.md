# ExcelRenderer Core

> Core reads saved formula results only. It has no dependency on the calculation adapter or formula engine. Use the main renderer or recalculate a workbook with OpenXml before passing it to Core.

English | [日本語](core.ja.md)

A standalone `netstandard2.1` source project for XLSX-to-PDF conversion, usable from .NET 8/10 through ProjectReference. Core has no separate NuGet package or embedded-font DLL and references only ExcelRenderer.OpenXml.Reader among product projects; it does not reference ExcelRenderer.OpenXml, Formulas, ExcelRenderer, Fonts or Tool. The main ExcelRenderer package includes Core DLL/XML from the same build.

Public types include `ExcelConverter`, `PdfExportOptions`, `ExcelReadOptions`, `ConversionResult`, and `ConversionDiagnostic` in the `ExcelRenderer.Core` namespace. Options and results follow the Core-specific scope described below.

```xml
<ItemGroup>
  <ProjectReference Include="../ExcelRenderer/src/ExcelRenderer.Core/ExcelRenderer.Core.csproj" />
</ItemGroup>
```

```csharp
using ExcelRenderer.Core;
using ExcelRenderer.OpenXml.Reader.Input;
using var xlsx = File.OpenRead("report.xlsx");
using var pdf = new FileStream("report.pdf", FileMode.Create, FileAccess.ReadWrite);
var result = await ExcelConverter.ConvertAsync(xlsx, pdf, new PdfExportOptions
{
    FontFilePath = "/fonts/NotoSansJP-Regular.ttf",
    SheetName = null,
    Input = new WorkbookInputOptions { MaxInputBytes = 128 * 1024 * 1024 },
});
```

## Input settings migration

`PdfExportOptions.Input` uses `ExcelRenderer.OpenXml.Reader.Input.WorkbookInputOptions` directly. The former public `ExcelRenderer.Core.WorkbookInputOptions` and Core's internal input preparer have been removed. This is a breaking API change: update explicit type names/imports to Reader.Input and rebuild consumers with matching product DLLs. No compatibility class or binary type alias is provided. `Input = new() { ... }` still works.

Reader settings remain a mutable `sealed class` with `set` properties. Replace `with` expressions on the old input record with a new `WorkbookInputOptions` instance and specify the required values. For example, given an existing `PdfExportOptions options`:

```csharp
var separate = options with
{
    Input = new WorkbookInputOptions { MaxInputBytes = 64 * 1024 * 1024 },
};
```

`PdfExportOptions` remains a record, but `options with { ... }` does not deep-copy Input. Do not mutate shared input settings during a conversion or concurrent conversions; create a separate instance for each conversion that needs different settings. Input's init accessor and default `new()` remain. The main library's `ExcelRenderer.Rendering.WorkbookInputOptions` record and the Mapping/OpenXml/Tool APIs retain their existing contracts. See [validation for this change](core-input-unification-validation.md).

## Supported behavior

All sheets, including hidden sheets, print in workbook order when both `SheetName` and `SheetNames` are null. Set `SheetNames = new[] { "Sheet3", "Sheet1" }` to select multiple sheets in output order; duplicates are removed. The existing `SheetName` still selects one sheet. Combining both properties, an empty list, empty/null names or unknown names throws `ArgumentException`. Empty sheets produce one page. Workbook print areas (including multiple areas), paper, margins, scaling/FitToPages, page order, print titles, manual breaks, cells, merges, backgrounds, borders, alignment, wrapping, ShrinkToFit, and existing header/footer rules remain supported. Date/time fields share one conversion snapshot. Cell rotation is ignored.

Pictures use their complete source image stretched into the placement rectangle. Position, size, and Z order remain supported. Only the page containing the picture's top-left point prints it; points outside the print area are omitted. Overflow is clipped at the PDF page boundary. Pictures are neither split nor repeated across pages or print titles. Each matching print area prints independently. Crop, rotation, and flips are ignored. Shapes, callouts, shape text, and PDF link annotations are omitted. Cell hyperlink display text remains; HYPERLINK formulas use CachedValue. In default CachedValue mode, formula displays use typed saved results from the original XML. OpenXml.Reader formats the immutable saved values directly. Missing caches display empty text with a diagnostic. Core has no formula mode or evaluation options; formula text and typed/shared saved results remain available as metadata. Non-formula cells retain ordinary formatting. Images require a complete successful pixel decode; partial or corrupt images are skipped with nonfatal diagnostics.

There is no format/range/page extraction/trim/continuous-image API and no PNG/SVG/Markdown renderer.

## One font

FontFilePath is required. Supply one static TrueType sfnt or select a static TrueType face in a TTC/OTC collection using FontCollectionIndex (zero-based, default 0). The selected face is normalized before measurement and drawing. Existing nonvariable color faces use the shared Skia color route. Extensions do not determine acceptance. CFF/CFF2, variable fonts, WOFF/WOFF2 and legacy true/typ1 remain unsupported for PDF. Missing files throw FileNotFoundException; unsupported/corrupt fonts throw InvalidDataException; invalid indices are argument errors. See [the current font contract](managed-truetype-subset.md).

The file is read once per conversion. The same snapshot supplies line metrics, text width, and PDF rendering. Column digit width uses that snapshot only when its physical family name matches the original workbook Normal family (case-insensitive). Otherwise, or when Normal metadata is absent, Core uses the same 7px/96-DPI compatibility MDW as the main renderer and reports `MaximumDigitWidthFallback`. A drawing-font substitution therefore no longer widens saved columns simply because its digits are wider. This fallback does not guarantee Excel geometry for an unavailable original font. Every family and bold/italic face becomes the supplied regular face, without simulations. Size, color, and underline remain. There is no OS/DLL exploration, fallback, or IVS glyph selection/private-use/color-emoji handling. Variation selectors (U+FE00–U+FE0F and U+E0100–U+E01EF) are removed from cell and header/footer text before measurement and drawing: an IVS sequence renders as its base character, without an extra selector glyph or advance. The base character still needs coverage in the supplied font. Missing characters follow the font/Skia's missing-glyph behavior; complete Unicode coverage is not promised. Text wrapping can still differ with the supplied drawing font. Column widths can differ from Excel when original Normal metrics are unavailable; Core never searches the OS for them.

## Streams and lifetime

Input starts at its current position, supports seekable and nonseekable streams, and remains open. Output must be writable. Seekable output must still be positioned at zero and empty. Nonseekable output is accepted without querying Position/Length or resetting it; the caller supplies a destination ready for a new PDF. Final output uses sequential awaited WriteAsync calls only, so ASP.NET Core Response.Body works with synchronous I/O disabled. Core does not flush or dispose the output; callers flush when needed. Both streams remain open on success, failure and cancellation.

Skia first generates the complete intermediate PDF in a seekable spool, using bounded memory then a temporary file. Structural analysis precedes final output; this does not deliver each page as soon as it is drawn. No additional complete final-PDF byte array or memory stream is created. Final syntax aggregation uses 64 KiB and payload transfer uses 80 KiB; fonts, images, layout and native resources have separate memory costs. Reading, layout and native drawing remain synchronous. Cancellation is checked at reads, page boundaries and awaited final writes. Synchronous third-party work cannot be interrupted instantly. Save failure/cancellation may leave partial output; the API does not promise atomic writing. Once an HTTP response starts, the host must handle failures without attempting to replace sent bytes with JSON. The [sample](../samples/ExcelRenderer.Core.Sample/Program.cs) writes a temporary file beside the destination, closes it, moves it after success, and removes it on failure.

```bash
dotnet run --project samples/ExcelRenderer.Core.Sample -c Release -- input.xlsx output.pdf /fonts/font.ttf
```

Defaults: input memory 16MiB, input limit 128MiB, ZIP entries 10,000, expanded ZIP limit 512MiB, temporary files disabled. Allowed temporary input spools are cleaned up on success/failure.

One page payload is generated at a time in a collection pass and a drawing pass. Core owns the shared Skia document lifecycle, subset primitives, basic drawing and bounded generated-PDF finalization. Text layouts cache at most 512 entries up to 2048 characters. Image-resource estimates use a 64MiB cache. The PDF spool allows temporary files after 8MiB and is cleaned on completion/failure. This is independent of the caller's input spool controls.

Core snapshots the supplied font once, uses its scalable hmtx advances for point layout, and uses the rounded 96-dpi MDW rule separately for a matching source family, or the diagnosed 7px compatibility metric for a substituted/unknown source family. Native font/subset resources are conversion-owned. There is no global font resolver, reset or conversion gate. Consecutive and mixed concurrent Core/main conversions select independent physical fonts.

Skia retains document resources until native close. The structural finalizer writes each active object and encoded stream once, adjusts boxes to 0.001pt precision, establishes RGB page compositing and retains interpolation metadata. Encoded image/font/content streams are copied without recompression; obsolete image revisions are omitted. See [image review fixes](pr90-image-review-fixes.md). SkiaSharp native assets are required; static TrueType subsetting and TTC extraction use Core C#; no Python/PDF-reader process is needed at runtime. Core remains nonpackable, with its assembly bundled by main/Tool.

## Provenance and verification

Sources were selected from main 1.7.2 at `32e9165d145516dbd0bcb4c2ae31a78ce6774bce`; see the [source map](core-source-map.md). PDFsharp and its unused cryptography pin have been removed; SkiaSharp 4.151.0 and the managed TrueType subsetter provide output. Fix shared algorithms in Core and regress both product policies. The source map records final ownership and product differences. Test font assets retain their OFL/copyright notices and are never embedded in the Core product.

See [validation results and reproduction](core-validation.md). Linux results do not establish Windows results; CI tests both platforms.

Header/footer handling inherits the existing field expansion and placement, without adding a parser for Excel formatting control codes. On Linux, Skia native initialization can access fontconfig. Rendering still selects only the supplied snapshot; absence of all native initialization accesses is not claimed.

## Shared implementation

Reader owns shared input preparation/spooling; Core uses its settings and preparer directly, while main retains its public settings adapter. The main library delegates image resources, workbook/cell traversal, style interning, geometry/page planning and building, text placement, cell-layer command generation, diagnostic aggregation and basic PDF drawing/saving to Core. The main assembly retains its public models, advanced font painters, image transforms, shapes, links, PNG/SVG/Markdown, selection/trim, and continuous orchestration.

Full reader/layout/drawing/PDF adapters provide the advanced behavior. Public main APIs stay in their existing assembly. See [the architecture](core-architecture.md), [verification results](core-refactor-validation.md) and [source ownership](core-source-map.md).

Main/Core use the same shared drawing/subset implementation while owning separate conversion resources. Distribute the main and Core DLLs from the same build. See [the removal validation](pdfsharp-removal-validation.md).

Core always uses OpenXml.Reader. Generic input/spool storage is shared through Reader. `ExcelReadOptions.FormulaDisplayMode`, `FormulaEvaluationOptions` and the Core `FormulaDisplayMode` enum have been removed; update callers and rebuild. Main rendering and CLI still support explicit recalculation. See [Reader split migration](openxml-reader-split.md).

Run `python3 scripts/validate-reader-isolation.py` to publish and execute separate Core and Reader consumers. It verifies the initial output, restore/runtime graphs and actual Core/Reader AssemblyRefs without deleting dependencies. The Core consumer generates a PDF from typed saved formula results and checks the missing-cache diagnostic.

Font collections use `PdfExportOptions.FontCollectionIndex` (zero-based, default 0). See [font support](managed-truetype-subset.md).

## Conditional formatting boundary

Core renders ordinary saved styles and never evaluates conditional formatting. The main library owns its optional evaluator; the shared reader only offers a generic no-op appearance completion hook before merged borders. Core has no CF option, formula dependency or calculation snapshot. See [main support](conditional-formatting.md).

## Try Core from the CLI

The existing ExcelRenderer.Tool package includes a `core` subcommand (no separate Core tool package):

```bash
excelrenderer core input.xlsx -o core.pdf --font-file ./regular.ttf --sheet Sheet3 Sheet1 --verbose
```

`--font-file` is required. `--font-collection-index` selects the zero-based face
(default: 0). `--sheet` accepts multiple names and can be repeated. Omitting it
selects all worksheets. The output must be a new file; failed conversions clean up
the temporary PDF. `--verbose` writes command details, elapsed time, conversion
metrics and full exceptions to standard error. Core diagnostics are always reported.
Optional `--data data.json` maps the template first using the main Mapping package;
the Core conversion itself still reads saved values and does not recalculate formulas.
