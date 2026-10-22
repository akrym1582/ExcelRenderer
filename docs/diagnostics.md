# Diagnostics and reliable PDF delivery

[日本語](diagnostics.ja.md) · [Main/Core comparison](main-core-parity.md) · [File sample](../samples/ExcelRenderer.Sample/README.md)

A successful conversion means the requested operation completed. It does not establish exact
Excel appearance. Absence of diagnostics does not guarantee support for omitted features.
Branch on `Code`, not the localized or evolving `Message`. Do not return messages or exception
text to HTTP clients: they can contain workbook metadata, formula details, paths or image bytes.

Main returns `ConversionResult` with `CompletionStatus`, pages, artifacts and diagnostics.
Normal success is `Completed`; diagnostic policy failure throws `ConversionException` with
diagnostics and completed artifacts. Many runtime errors are wrapped in that exception;
argument errors and cancellation propagate. A thrown operation must not be treated as a
successful file, even if some bytes or completed artifacts exist.

Main diagnostics have `Code`, `Severity`, `Stage`, `Message`, `SheetName`, `CellRange`,
`ObjectId`, `SourcePageNumber`, `OccurrenceCount`, and `UnicodeSequence`. Location fields
may be null; page numbers identify source pages. Core returns `PageCount` and diagnostics
with only `Code`, `Message`, `SheetName`, `PageNumber`. Core exposes neither severity nor
occurrence counts nor the main diagnostic policy API.

| Main `DiagnosticOptions` | Default and effect |
| --- | --- |
| EnableLayoutWarnings | false; report finalized clipping and small text for selected output pages |
| MinimumReadableFontSize | 6pt, positive finite; output size after scale/ShrinkToFit |
| DetectUnsupportedFeatures | false; collect additional unsupported-feature reports, not a guarantee that all omissions are detected |
| StrictMode | false; any non-Info diagnostic fails |
| TreatAsErrors | empty; listed codes fail, including Info |
| SuppressedCodes | empty; omit codes from returned collection, **does not undo failure** |
| MaxDiagnostics | 100, positive; maximum retained unique identities, followed by truncation summary |

Failure is evaluated before retention, suppression and limits. Error always fails; StrictMode
promotes warnings; TreatAsErrors also promotes Info. Duplicate identities (code, sheet,
cell range, object, source page, Unicode sequence) merge occurrence counts. Once full, new
identities increment the discarded count and `DiagnosticsTruncated` is appended; retained
identities still merge. The store checks capacity before suppression for a new identity,
so a suppressed occurrence arriving at capacity can contribute to the truncation count.
The truncation summary itself is generated during result materialization, not added as a new
policy event. Suppression is not a way to allow a failed operation.

Cell-scoped reader diagnostics, layout warnings and ranged unsupported features follow
selected sheets/ranges/pages; sheet-level/read-global diagnostics can remain without a page.
Selection is not a global diagnostic suppression switch. Main performs preflight before opening
the output sink where possible, but late rendering/output failures can leave partial bytes.
These rules are covered by [DiagnosticPolicyTests](../tests/ExcelRenderer.Tests/DiagnosticPolicyTests.cs),
[LayoutDiagnosticTests](../tests/ExcelRenderer.Tests/LayoutDiagnosticTests.cs),
[OpenXmlEndToEndTests](../tests/ExcelRenderer.Tests/OpenXmlEndToEndTests.cs), and
[ImageReviewTests](../tests/ExcelRenderer.Tests/ImageReviewTests.cs).

| Code | Main / Core | Meaning and output impact | Action / related tests |
| --- | --- | --- | --- |
| MaximumDigitWidthFallback | Both | Original Normal metrics unavailable; 7px compatibility MDW may change columns/pages | Supply source metrics in main, preserve Normal metadata; ColumnWidthMetricsTests, MainCoreParityTests |
| MaximumDigitWidthResolved | Main Info / Core no notification | Main records resolved/override MDW decision | Inspect source metric decision; ColumnWidthMetricsTests |
| FormulaCachedValueMissing | Both | Formula cache absent, displayed empty in saved-value mode | Re-save after calculation or explicitly choose main limited recalculation; FormulaDisplayTests, MainCoreParityTests |
| ImageDecodeFailed | Both | Complete decode failed; image skipped | Replace damaged/unsupported image; ImageReviewTests, Core ImagePayloadTests / PageAndImageTests |
| MissingIvsGlyph | Main / Core no notification | Requested variation glyph unavailable | Supply IVS-capable font or explicitly allow base replacement; ShapeAndFontTests |
| MissingPrivateUseGlyph | Main / Core no notification | Private-use glyph unavailable | Supply intended font; ShapeAndFontTests |
| CellTextClipped | Main opt-in / Core no notification | Final text exceeds cell clip | Widen/enlarge cells, wrap or revise scaling; LayoutDiagnosticTests |
| CellTextTooSmall | Main opt-in / Core no notification | Output text below configured threshold | Reduce compression or change layout; LayoutDiagnosticTests |
| UnsupportedShape / UnsupportedDrawingObject | Main detection / Core no notification, omitted by contract | Unsupported drawing skipped | Replace drawing or validate output visually; ShapeAndFontTests / UnsupportedChartTests |
| UnsupportedRichTextFormatting / RichTextLayoutFallback | Main detection / Core no notification, flattened by contract | Partial decoration or supplied measurement service loses run formatting | Review font/layout service; RichTextFallbackTests / RichTextAcceptanceTests |
| ConditionalFormattingLimitExceeded | Main / Core not applicable | Bounded rule/population work exceeded; ordinary formatting retained in affected scope | Simplify sqref/rules/data; ConditionalFormattingTests.LimitTargets/Phase2 |
| DiagnosticsTruncated | Both | More diagnostic identities than retained capacity | Do not infer completeness from the list; main can increase MaxDiagnostics; DiagnosticPolicyTests |

There is no general diagnostic for every ordinary missing character or font-family substitution.
Core does not diagnose every intentionally omitted shape, link, rich run, rotation or conditional
format. Verify required glyph coverage and supported scope separately. Its diagnostic list is
internally bounded at 100 unique identities plus summary; it is not an exhaustive audit API.

Choose application rejection codes explicitly and branch on Code. Main can fail through
DiagnosticOptions; Core callers inspect result.Diagnostics themselves. To inspect diagnostics
before publishing, convert to a temporary final PDF first, check the result, then publish or
remove that file. This adds disk I/O and delays delivery; the application file is separate from
the library's intermediate spool. The file samples publish completed conversions and report
diagnostics; they do not impose a business-specific rejection policy.

| Delivery | Use when | Diagnostic handling |
| --- | --- | --- |
| Direct caller-owned stream | Warnings may accompany output | Diagnostics arrive after conversion; sent bytes cannot be withdrawn |
| Inspect before publishing | Specific codes must prevent delivery | Inspect a temporary final PDF's result, then publish or delete |
| File conversion sample | Preserve the previous output on failure | Close the temporary file before replacing the destination |

For HTTP integration in your own application, leave Response.Body open and forward cancellation.
After response start, let the host handle exceptions without appending JSON to a partial PDF.
Classify client input separately from server-owned fonts/settings: Core invalid fonts can also
throw InvalidDataException, so that exception must not be mapped wholesale to a client error.
Cancellation is distinct from conversion failure. CPU reading/layout/native drawing remain
synchronous; asynchronous final writes do not make them interruptible immediately.

Input defaults in both APIs: 128MiB maximum input, 16MiB memory threshold, temporary input
files disabled, 10,000 ZIP entries, 512MiB total declared expanded ZIP bytes. Configure limits
for your workload; these are not a complete process-memory budget. Main has `Input` and PDF
`Buffering`; Core has Reader `Input`, and its PDF intermediate spool is internally 8MiB then
temporary file. Native fonts/images/layout use additional memory. Keep application temp directories
writable with adequate disk space and access controls; always close files before replacement.

Libraries leave caller input/output streams open. Main's SingleStreamOutputSink flushes at
completion; Core does not flush the caller output. Pass RequestAborted/CancellationToken through
all conversion/copy calls. [Main file example](../samples/ExcelRenderer.Sample/PdfFileExample.cs)
and [Core file example](../samples/ExcelRenderer.Core.Sample/Program.cs) illustrate ownership,
cleanup, page counts and cancellation with executable code.
