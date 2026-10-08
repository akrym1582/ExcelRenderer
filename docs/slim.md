# ExcelRenderer Slim

A standalone `netstandard2.1` source project for XLSX-to-PDF conversion, usable from .NET 8/10 through ProjectReference. No NuGet package, CLI, embedded-font DLL, or reference to ExcelRenderer/Fonts/Tool is provided.

Public type names match the original library: `ExcelConverter`, `PdfExportOptions`, `WorkbookInputOptions`, `ConversionResult`, and `ConversionDiagnostic`. They are in the `ExcelRenderer.Slim` namespace; option and result members retain the Slim-specific scope. Update callers using the former `Slim`-prefixed names to these names.

```xml
<ItemGroup>
  <ProjectReference Include="../ExcelRenderer/src/ExcelRenderer.Slim/ExcelRenderer.Slim.csproj" />
</ItemGroup>
```

```csharp
using ExcelRenderer.Slim;
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

Input starts at its current position, supports seekable and nonseekable streams, and remains open. Output must be writable, seekable, positioned at zero, and empty; it also remains open. No complete intermediate PDF buffer is created. Cancellation is checked at reads, page boundaries, and PDF write boundaries; synchronous third-party work cannot be interrupted instantly. Save failure/cancellation may leave partial output; the API does not promise atomic writing. The [sample](../samples/ExcelRenderer.Slim.Sample/Program.cs) writes a temporary file beside the destination, closes it, moves it after success, and removes it on failure.

```bash
dotnet run --project samples/ExcelRenderer.Slim.Sample -c Release -- input.xlsx output.pdf /fonts/font.ttf
```

Defaults: input memory 16MiB, input limit 128MiB, ZIP entries 10,000, expanded ZIP limit 512MiB, temporary files disabled. Allowed temporary input spools are cleaned up on success/failure.

One page payload is generated at a time, appended to one final PdfDocument, and saved once. Text layouts have a conversion-local cache limited to 512 entries and strings up to 2048 characters. Decoded-image estimates have a 64MiB cache with leases and disposal. ClosedXML and the final PDF document still retain their models/resources; PDFsharp document image retention is separate from the cache budget. Constant RSS or a memory reduction percentage is not guaranteed. SkiaSharp native libraries remain required for image decode, line metrics, and digit widths.

Slim serializes font reset, measurements, drawing, Save, and document disposal with its own cancellation-aware semaphore. Consecutive/concurrent Slim calls with different fonts are supported. Other libraries or the full renderer modifying PDFsharp's global font settings in the same process are outside the initial support scope. Reference comparisons use separate processes.

## Provenance and verification

Sources were selected from main 1.7.2 at `32e9165d145516dbd0bcb4c2ae31a78ce6774bce`; see the [source map](slim-source-map.md). Dependencies keep the same versions. Shared bug fixes must be checked in both the original and Slim copies. Test font assets retain their OFL/copyright notices and are never embedded in the Slim product.

See [validation results and reproduction](slim-validation.md). Linux results do not establish Windows results; CI tests both platforms.

Header/footer handling inherits the existing field expansion and placement, without adding a parser for Excel formatting control codes. On Linux, Skia native initialization can access fontconfig. Rendering still selects only the supplied snapshot; absence of all native initialization accesses is not claimed.
