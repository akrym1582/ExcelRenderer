# CLI metric aggregation

Every CLI command prints `Metric name: value` to standard output. Observations are
aggregated within one command invocation, sorted by name, and emitted even when
conversion fails. A missing metric means no observation was reported; it does not
imply zero. Units remain encoded in the existing metric names.

| Observation | CLI aggregation | Examples |
| --- | --- | --- |
| Phase duration | Sum of reported durations | `reader.ms`, `pageBuild.ms`, `pdfSubset.ms` |
| Event or work increment | Sum | `commands`, `imageDecode`, `imageCacheHit`, `corePageBuild`, `typefaceCreated`, `typefaceDisposed`, `textWrapComparisons`, `resolvedBorderSegments` |
| Bytes reported per font or generated SVG | Sum | `fontsBytesLoaded`, `pdfOriginalFontBytes`, `pdfSubsetFontBytes`, `postScript.fontBytes`, `svgBufferBytes` |
| Spill event | Sum | `buffer.spillCount`, `svg.spillCount`, `postScript.spillCount` |
| Bounded-cache events | Sum | `textWidthCacheHits`, `textLineCacheMisses`, `richTextLayoutCacheEvictions`, cache `Scans` and `Bypasses` |
| Resource size or reported peak | Maximum observation | `pngBitmapBytes`, `imageCacheEstimatedBytes`, `svgBufferMemoryPeakBytes`, `postScript.bufferMemoryPeakBytes`, `layoutCacheEntries`, `layoutCacheRetainedUnits`, any `.max` |
| Current cache occupancy | Last observation, including zero | Any `.current` |
| Producer snapshot or identifier | Last observation | `cells`, `model.cells`, `modelSelectedCells`, `renderCells`, `pageCandidateCount`, `readerStyleCacheEntries`, `readerStyleCacheHits`, `readerStyleConversions`, `pdfBackend`, `postScript.bytes`, `postScript.fontCount`, `managedAllocatedBytes` |

`readerStyleCacheHits` is already accumulated by the reader, unlike the one-event
cache hit reports. It must not be treated as an event increment. Likewise,
conditional-formatting evaluation/candidate/work snapshots retain their last
reported values; population, sort, value-read and rule-plan events accumulate.
The exact policies live in [MetricSummary.cs](../src/ExcelRenderer.Tool/Commands/MetricSummary.cs).
Unknown future metrics preserve their last value until their reporting contract
is reviewed. New durations use `.ms`, occupancy snapshots use `.current`, and
peak observations use `.max` where possible.

`pagePayloadActive` reports the final number of active payloads. Both Main and Core
report `+1` when taking ownership and `-1` when releasing it. The CLI uses the same
aggregation for both products. `pagePayloadMax` is the largest observed active
count. Successful conversions should finish with active count zero.

`commands` is the cumulative count of logical drawing commands generated for the
output pass across selected pages/sheets, in both Main and Core. Preflight and
font collection are excluded. Counting streams commands without retaining another
page buffer, and also covers continuous PNG/SVG output. Viewport wrappers used for
trimming are excluded by counting the commands before wrapping them. This is not
a count of PDF operators, glyphs or native draw calls: one logical command can
produce multiple such operations. On failure it includes commands yielded before
failure, even if the artifact was subsequently rolled back.

`renderCells` remains the last page-build snapshot. Build/event counts describe
actual work, including repeated passes, rather than unique pages.

A resource maximum is the maximum **reported** size, not total process memory or
the sum of simultaneous native allocations. `managedAllocatedBytes` is the
process-wide managed allocation delta during the command, including GC-collected
allocations and excluding native memory.

Phase timings can be nested. Do not sum all `.ms` fields to derive total elapsed
time; use the command's `Completed in ... ms` output for wall-clock elapsed time.

## Page and sheet summaries

The CLI also prints one `Page "sheet" source=N output=M: ...` line per attempted
rendered page and one `Sheet "sheet": pages=N ...` line per rendered sheet. Sheet
order follows rendering order. The page numbers identify the page in the source
sheet and its position in the selected output, respectively. Continuous canvases
use `output=continuous` because they do not have a document output-page number. Sheet names are
JSON-escaped so each summary occupies one console line.

Every summary includes `renderMs` and `managedAllocatedBytes`. Available
representative fields also include `commands`, `buildMs`, `textMs`, `glyphCalls`,
`glyphChars`, `imageDecodes`, `imageCacheHits`, `encodeMs`, `bitmapPeakBytes`,
`imageCachePeakBytes` and `payloadPeak`. Missing fields had no observation in that
scope. `glyphCalls` counts glyph-measurement calls and `glyphChars` counts UTF-16
code units supplied to those calls, rather than final shaped glyphs. Sheet counters
sum the rendered pages; resource sizes retain the largest
observed value. The same aggregation rules apply to Main and Core.

These summaries cover page generation and drawing only. Reading, preflight/font
collection, document-wide font preparation and final document saving remain in
the command-wide metrics. Page allocation deltas cover all process threads while
the page scope is active and do not include native allocations. Sheet allocation
and elapsed values sum its page scopes; they are not whole-conversion totals.
A page whose generation/drawing fails still prints the measurements collected so
far and counts as an attempted page, even if its artifact is rolled back.
