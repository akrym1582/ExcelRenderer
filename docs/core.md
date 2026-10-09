# ExcelRenderer Core

A standalone `netstandard2.1` source project for XLSX-to-PDF conversion, usable from .NET 8/10 through ProjectReference. Core has no separate NuGet package or embedded-font DLL and does not reference ExcelRenderer/Fonts/Tool. The main ExcelRenderer package includes Core DLL/XML from the same build.

Public type names match the original library: `ExcelConverter`, `PdfExportOptions`, `WorkbookInputOptions`, `ConversionResult`, and `ConversionDiagnostic`. They are in the `ExcelRenderer.Core` namespace; option and result members retain the Core-specific scope. When migrating from ExcelRenderer.Slim, change the ProjectReference and `using ExcelRenderer.Slim;` to Core. No Slim wrapper or old DLL is produced.

```xml
<ItemGroup>
  <ProjectReference Include="../ExcelRenderer/src/ExcelRenderer.Core/ExcelRenderer.Core.csproj" />
</ItemGroup>
```

```csharp
using ExcelRenderer.Core;
using var xlsx = File.OpenRead("report.xlsx");
using var pdf = new FileStream("report.pdf", FileMode.Create, FileAccess.ReadWrite);
var result = await ExcelConverter.ConvertAsync(xlsx, pdf, new PdfExportOptions
{
    FontFilePath = "/fonts/NotoSansJP-Regular.ttf",
    SheetName = null,
});
```

## Supported behavior

All sheets, including hidden sheets, print in workbook order unless one SheetName is supplied. Empty sheets produce one page. Workbook print areas (including multiple areas), paper, margins, scaling/FitToPages, page order, print titles, manual breaks, cells, merges, backgrounds, borders, alignment, wrapping, ShrinkToFit, and existing header/footer rules remain supported. Date/time fields share one conversion snapshot. Cell rotation is ignored.

Pictures use their complete source image stretched into the placement rectangle. Position, size, and Z order remain supported. Only the page containing the picture's top-left point prints it; points outside the print area are omitted. Overflow is clipped at the PDF page boundary. Pictures are neither split nor repeated across pages or print titles. Each matching print area prints independently. Crop, rotation, and flips are ignored. Shapes, callouts, shape text, and PDF link annotations are omitted. Cell hyperlink display text remains; HYPERLINK formulas use CachedValue. General cells retain GetFormattedString behavior, without adding a formula engine or promising Excel recalculation equivalence. Image decode failures are returned as nonfatal diagnostics.

There is no format/range/page extraction/trim/continuous-image API and no PNG/SVG/Markdown renderer.

## One font

FontFilePath is required. The initial format is one static TrueType-outline sfnt, validated by header/table bounds and by Skia/PDFsharp loading before pages are drawn. Extensions are not used for acceptance. TTC/OTC, CFF OTF, variable fonts, WOFF, and TTE are unsupported. Missing files throw FileNotFoundException; unsupported/corrupt fonts throw InvalidDataException.

The file is read once per conversion. The same snapshot supplies column digit width, line metrics, text width, and PDF rendering. Every family and bold/italic face becomes the supplied regular face, without simulations. Size, color, and underline remain. There is no OS/DLL exploration, fallback, or IVS glyph selection/private-use/color-emoji handling. Variation selectors (U+FE00–U+FE0F and U+E0100–U+E01EF) are removed from cell and header/footer text before measurement and drawing: an IVS sequence renders as its base character, without an extra selector glyph or advance. The base character still needs coverage in the supplied font. Missing characters follow the font/PDFsharp's normal behavior; complete Unicode coverage is not promised. Font replacement may change column widths and wrapping from the original workbook.

## Streams and lifetime

Input starts at its current position, supports seekable and nonseekable streams, and remains open. Output must be writable, seekable, positioned at zero, and empty; it also remains open. No complete intermediate PDF buffer is created. Cancellation is checked at reads, page boundaries, and PDF write boundaries; synchronous third-party work cannot be interrupted instantly. Save failure/cancellation may leave partial output; the API does not promise atomic writing. The [sample](../samples/ExcelRenderer.Core.Sample/Program.cs) writes a temporary file beside the destination, closes it, moves it after success, and removes it on failure.

```bash
dotnet run --project samples/ExcelRenderer.Core.Sample -c Release -- input.xlsx output.pdf /fonts/font.ttf
```

Defaults: input memory 16MiB, input limit 128MiB, ZIP entries 10,000, expanded ZIP limit 512MiB, temporary files disabled. Allowed temporary input spools are cleaned up on success/failure.

One page payload is generated at a time, appended to one final PdfDocument, and saved once. Text layouts have a conversion-local cache limited to 512 entries and strings up to 2048 characters. Decoded-image estimates have a 64MiB cache with leases and disposal. ClosedXML and the final PDF document still retain their models/resources; PDFsharp document image retention is separate from the cache budget. Constant RSS or a memory reduction percentage is not guaranteed. SkiaSharp native libraries remain required for image decode, line metrics, and digit widths.

Core and the main renderer share one cancellation-aware font gate for font reset, measurement, drawing, Save and disposal. Consecutive/concurrent calls with different fonts are supported in the same load context. Unrelated PDFsharp users and separately loaded Core DLLs are outside this guarantee. Reference comparisons use separate processes.

## Provenance and verification

Sources were selected from main 1.7.2 at `32e9165d145516dbd0bcb4c2ae31a78ce6774bce`; see the [source map](core-source-map.md). Dependencies keep the same versions. Fix shared algorithms in Core and regress both product policies. The source map records final ownership and product differences. Test font assets retain their OFL/copyright notices and are never embedded in the Core product.

See [validation results and reproduction](core-validation.md). Linux results do not establish Windows results; CI tests both platforms.

Header/footer handling inherits the existing field expansion and placement, without adding a parser for Excel formatting control codes. On Linux, Skia native initialization can access fontconfig. Rendering still selects only the supplied snapshot; absence of all native initialization accesses is not claimed.

## Shared implementation and migration status

The main library delegates input preparation/spooling, image resources, workbook/cell traversal, style interning, geometry/page planning and building, text placement, cell-layer command generation, diagnostic aggregation and basic PDF drawing/saving to Core. The main assembly retains its public models, advanced font painters, image transforms, shapes, links, PNG/SVG/Markdown, selection/trim, and continuous orchestration.

Full reader/layout/drawing/PDF adapters provide the advanced behavior. Public main APIs stay in their existing assembly. See [the architecture](core-architecture.md), [verification results](core-refactor-validation.md) and [source ownership](core-source-map.md).

Main/Core converters share a font gate when using the same Core DLL in one load context. External PDFsharp users and separately loaded Core copies are outside that synchronization boundary. Distribute the main and Core DLLs from the same build.
