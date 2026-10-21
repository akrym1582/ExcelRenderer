# Asynchronous final PDF output validation

本体の RenderAsync と Core の ConvertAsync は、非 Seek・同期 I/O 禁止の HTTP 応答 Stream に
最終 PDF を直接非同期出力できます。日英の利用ガイド、実行可能な HTTP サンプル、回帰テストを更新しました。
中間スプールと同期 CPU 処理は残ります。今回の測定では高速化を確認していません。

## Implementation and limits

Local initial implementation commit: `53a4b24f0d07158494b2c2bd8de74c775581ea2f`; initial runtime/test metadata is in
[checks.json](validation/async-pdf/checks.json). The initial validation below used main
`f73676c437bc991384dc04c8b19d48d6f1fbff4e`, before the PDF read-buffer optimization merged.

The draft also integrates main `4f9314e46685ddfafe3102a796c0fb8550e2fe31` (including #104).
Both copy paths now use PdfSyntaxReader's shared BeginCopy invalidation and logical/physical
cursor updates. Async reads and writes finish before the next block; source movement never
bypasses the syntax-buffer owner. New tests restore syntax reads after successful/canceled
async copies and validate bounds/truncation. See the integration recheck appended below.

GeneratedPdfFinalizer uses one lazy output-element iterator for page/CropBox/group/link/image
corrections, object order and logical xref offsets. Sync and async consumers share that algorithm.
The async consumer aggregates syntax in 65,536 bytes and reuses an 81,920-byte payload buffer;
it awaits each operation before requesting the next element. Payloads remain encoded, without
an added complete final-PDF byte array, MemoryStream, background queue or per-object flush.
Existing parser dictionaries/object records and per-object serialization still consume memory.

Skia Close and flushing its internal seekable spool remain synchronous. Final destination writes
are asynchronous. Main counts successful writes, awaits one final FlushAsync, then completes the
artifact. Core accepts nonseekable outputs without Position/Length/Seek/SetLength and leaves
flushing to the caller. Seekable Core outputs must still be empty at position zero. Both APIs
preserve caller-owned input/output streams. Abort cleanup uses CancellationToken.None; a secondary
cleanup exception remains on the original exception's Data["AbortException"]. Native/spool/font
owners remain alive until awaited completion and are disposed on failure/cancellation.

CPU reading/layout/drawing is unchanged. Transmission begins after intermediate generation and
structural analysis; first-page delivery is not immediate. Failure after HTTP transmission starts
can leave partial bytes, requiring host-side exception/connection handling. The sync PdfRenderer
API remains supported; other output formats retain their existing output behavior.

## Initial executed checks

Linux x64 Debian 13, SDK 10.0.401, runtime 10.0.12. Linux solution tests sourced
`scripts/test-fonts.sh`. Builds, tests, packing and benchmark rendering ran sequentially.
Windows execution is left to the existing CI matrix; no local Windows result is claimed.

| Check | Result |
| --- | --- |
| Solution restore | Passed |
| Debug solution Rebuild | 0 warnings, 0 errors |
| Full solution test | 2,683 passed, 0 failed, 0 skipped |
| Release solution Rebuild | 0 warnings, 0 errors |
| Standalone Core Release coverage gate | 356 passed; line 82.36%, branch 70.35% (thresholds unchanged: 82.12%, 70.08%) |
| Core Release Performance category | 8 passed without coverage |
| Focused async/finalizer tests | 38 Core and 11 main cases passed, including both real Kestrel endpoints |
| Namespace alignment | 689 declarations; 2 compiler shims |
| Four-package pack/distribution | Exactly 4 packages; shared DLL/XML hashes match |
| Reader/Core isolation | Separate restore/build/publish, graph/AssemblyRef and process execution passed |
| Isolated package/font/CLI consumers | No implicit font package; 3 packaged families; matching CLI fonts and installed CLI passed |
| PDFsharp prohibition | 1,269 source/config files, 24 restored projects, 790 AssemblyRef inventories, 4 packages passed |

The tests reject synchronous destination Write/WriteByte/Flush, reads, seeks, length/position
access and SetLength. Controlled TaskCompletionSource gates assert that conversion/metrics do
not finish while a write is blocked, only one write is pending, and no bytes arrive before release.
They verify cancellation while blocked, write/flush/open/complete failures, abort secondary errors,
caller ownership, and memory/file-spool cleanup. Copy tests include short async reads and truncation.
Deterministic generated PDFs produce byte-identical sync/async results, including dimensions,
links, generations, metadata, xref and encoded image payloads. Existing independent PDF/image/font,
link/SMask, sync renderer and CLI regressions ran in the full solution suite.

The [HTTP sample](../samples/ExcelRenderer.Http.Sample) handlers themselves run in the Kestrel
tests, bound to a dynamic loopback port with AllowSynchronousIO=false. Server exceptions are
captured; HttpClient checks 200/application/pdf and strict PdfPig parsing/page text for both APIs.

Packing used `dotnet pack src/<package>/<package>.csproj -c Release --no-restore` for each of the
four distribution projects. The shared bundling targets require builds, so solution-wide
`pack --no-build` is not a supported verification command here.

## Initial recorded performance

[Before](validation/async-pdf/before.jsonl), [after](validation/async-pdf/after.jsonl), and
[per-case medians](validation/async-pdf/summary.json) retain one warm-up plus three measured
rounds for every workload/mode. Environment records include DLL/input/font SHA-256 hashes.
Both saved Release runtimes used the same harness and fixtures, without overlapping build/test
activity. Before uses the baseline's synchronous final save; after uses this branch's async save.
Only the benchmark harness/framework reference was copied to the baseline worktree.

Elapsed milliseconds (three-round median; individual samples are retained):

| Fixture | Before file | After file | After staged HTTP | After direct HTTP | PDF bytes |
| --- | ---: | ---: | ---: | ---: | ---: |
| 01-japanese | 36.38 | 37.11 | 44.22 | 54.99 | 119,165 |
| 06-layout-and-pagination | 47.70 | 57.10 | 49.69 | 53.54 | 119,480 |
| 02-image | 36.49 | 36.68 | 44.93 | 46.18 | 120,712 |
| 02-image, forced spool spill | 39.04 | 44.56 | 42.25 | 46.39 | 120,712 |

Output sizes match before/after. The forced-spill workload records one spool spill in every mode.
Staged HTTP writes an extra complete final file of 119,165–120,712 bytes; direct HTTP writes none.
The existing intermediate PDF spool remains in both cases.

Raw records include whole-conversion time, pdfFinalSave, process-wide managed allocations,
output bytes, spool counts, extra final file bytes and process thread snapshots. File allocation
medians increased by about 55–97 KB, consistent with adding a 64 KiB syntax buffer and async
bookkeeping. HTTP allocation measurements include the loopback client and its response byte
array, so they are not server-memory measurements. Thread snapshots (18 before, 19 after) do
not establish blocked-thread occupancy. The controlled sink tests prove bounded in-flight I/O.

These short three-round probes do not demonstrate a latency improvement or a regression bound.
Direct response support with synchronous I/O disabled and awaited backpressure is the acceptance
result. CPU speedups, concurrency/throughput gains and process-memory reductions are not claimed.
No read-parser optimization was mixed into this comparison.

Reproduction: see [harness instructions](../tools/ExcelRenderer.Performance/README.md#async-pdf-output).
Saved runtimes remain under `artifacts/async-pdf/{before-runtime,after-runtime}` in this workspace.
Build/test/distribution logs, inventories, independent-consumer results and raw samples are
retained in [validation/async-pdf](validation/async-pdf). `artifact-hashes.json` records their integrity.


## Integration recheck against buffered-reader main

The final draft integrates main `4f9314e46685ddfafe3102a796c0fb8550e2fe31`, including the
preceding read optimization. Tested local source `03baa87a3daa697f389a34192838ea10fa3e8257`
and published source `712985942166a182cfbfd0a7b2329a2c9dfe77ff` have identical Git trees.
[Integration metadata](validation/async-pdf/integration-checks.json) records the source tree,
final runtime hashes and independent recheck results. Subsequent updates contain documentation
and evidence only. The draft is [PR #105](https://github.com/akrym1582/ExcelRenderer/pull/105).

- Solution tests: **2,722 passed**, 0 failed/skipped; including all async-only/Kestrel cases.
- Debug and Release Rebuild: **0 warnings, 0 errors**.
- Core Release coverage: **391 passed; line 82.76%, branch 71.08%**; unchanged thresholds passed.
- Core Performance without coverage: **8 passed**.
- Four-package packing, matching shared DLL/XML files, isolated package/font/CLI consumers,
  Reader/Core standalone consumers, and dependency audit passed again.
- Namespace verification: **692 declarations**, 2 compiler shims.

CopyStreamAsync now delegates to PdfSyntaxReader.CopyRangeAsync. Both range-copy methods
share validation, read-ahead invalidation and initial cursor movement. After every asynchronous
read, logical and physical cursors advance before awaiting the destination write. Success,
short reads, cancellation and failure retain consistent cursor ownership; syntax reads can
restore an earlier logical position safely. Added direct cursor tests exercise these cases.
The evidence manifest uses neutral dependency-audit filenames to respect the existing reserved
backend-name scanner; no scanner exception or suppression was added.

The initial samples above remain evidence for the pre-integration implementation. Fresh
[optimized baseline](validation/async-pdf/optimized-before.jsonl),
[integrated async](validation/async-pdf/optimized-after.jsonl), and
[medians](validation/async-pdf/optimized-summary.json) use the same buffered-reader main baseline
on both sides. Each retains one warm-up and three rounds, input/font/DLL hashes, allocations,
final-save timings, sizes, spills and thread snapshots. No build/test overlapped rendering.

| Fixture | Buffered main file ms | Integrated async file ms | Integrated staged HTTP ms | Integrated direct HTTP ms |
| --- | ---: | ---: | ---: | ---: |
| 01-japanese | 36.52 | 31.70 | 36.42 | 45.88 |
| 06-layout-and-pagination | 44.32 | 43.30 | 43.78 | 43.39 |
| 02-image | 43.21 | 38.28 | 38.61 | 37.75 |
| 02-image, forced spool spill | 45.31 | 38.18 | 41.33 | 39.39 |

Output sizes still match the initial table, and forced-spill cases record one spool spill.
Direct HTTP again eliminates the extra complete final file; the intermediate spool remains.
File allocation medians increase by approximately 64–113 KB. These short sequential probes
remain descriptive; they do not establish a speedup, throughput claim or regression bound.
Saved integrated runtimes are under `artifacts/async-pdf/optimized-{before,after}-runtime`.
