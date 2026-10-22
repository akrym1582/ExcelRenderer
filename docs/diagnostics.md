# Diagnostics and reliable PDF delivery

[日本語](diagnostics.ja.md) · [Main/Core comparison](main-core-parity.md) · [HTTP sample](../samples/ExcelRenderer.Http.Sample/README.md)

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

The compiled [HTTP handler](../samples/ExcelRenderer.Http.Sample/PdfHttpEndpoints.cs) explicitly
rejects `FormulaCachedValueMissing` and `ImageDecodeFailed` in validated mode after obtaining the
result. It groups main occurrence counts and Core retained entries by code; these counts have
different granularity. The missing-cache diagnostic is covered by
[MainCoreParityTests](../tests/ExcelRenderer.Tests/MainCoreParityTests.cs), and image decode
failures by [ImageReviewTests](../tests/ExcelRenderer.Tests/ImageReviewTests.cs). Other codes
are logged and allowed. An application with stricter completeness requirements should also
consider `DiagnosticsTruncated` and its supported-feature contract; these two codes are an
example business policy, not a complete validation of arbitrary spreadsheets.

| Delivery | Use when | Diagnostic rejection and cost |
| --- | --- | --- |
| /pdf, /core-pdf | Result can be delivered with warnings | Log after conversion; final output may already have been sent |
| /pdf-validated, /core-pdf-validated | Refuse specific diagnostics before delivery | Convert to unique final-PDF file, inspect result, then copy; extra disk I/O and later first response byte |
| File conversion | Keep previous file on failure | Generate adjacent temporary, close, check cancellation, replace after success |

Validated rejection is 422 ProblemDetails containing only codes/counts. Output is copied with
`CopyToAsync` before disposing/deleting the file; all failure/cancel paths clean up, and
Response.Body remains caller-owned. The application temporary **final PDF** is separate from
the library's intermediate spool. No full PDF `ToArray`/MemoryStream copy is used by the samples.

This sample reads server-owned paths, so malformed/missing XLSX, missing/invalid configured
fonts, I/O and conversion failures before response start are 500, with a generic title.
A future upload endpoint could map independently identified bad client input/limits to 400/413;
it must not map every InvalidDataException to 400 because Core's invalid font is also invalid
data. Upload handling is not part of this sample. Diagnostic policy rejection remains 422.
Cancellation propagates separately and is logged at Information; the host owns disconnect behavior.

After `Response.HasStarted`, exceptions propagate to the host: no JSON is appended. Clients can
receive a partial PDF even with an initial 200. Check transport completion and PDF validity;
retry only under application policy (for example a transient disconnect), not repeatedly for
missing caches, invalid fonts or deterministic rejections. A direct response cannot be withdrawn
after diagnostics arrive. CPU reading/layout/native drawing remain synchronous; asynchronous
final writes do not make them interruptible immediately and do not use Task.Run.

Input defaults in both APIs: 128MiB maximum input, 16MiB memory threshold, temporary input
files disabled, 10,000 ZIP entries, 512MiB total declared expanded ZIP bytes. Configure limits
for your workload; these are not a complete process-memory budget. Main has `Input` and PDF
`Buffering`; Core has Reader `Input`, and its PDF intermediate spool is internally 8MiB then
temporary file. Native fonts/images/layout use additional memory. Keep application temp directories
writable with adequate disk space and access controls; always close files before replacement.

Libraries leave caller input/output streams open. Main's SingleStreamOutputSink flushes at
completion; Core does not flush the caller output. Pass RequestAborted/CancellationToken through
all conversion/copy calls. [Main file example](../samples/ExcelRenderer.Http.Sample/PdfFileExample.cs)
and [Core file example](../samples/ExcelRenderer.Core.Sample/Program.cs) illustrate ownership,
cleanup, page counts and cancellation with executable code.
