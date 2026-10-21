# Buffered generated-PDF reads

This implements the supplied PDF read optimization plan against main
`f73676c437bc991384dc04c8b19d48d6f1fbff4e`, fetched at the start of work.

`SpillableBufferStream.ReadByte()` delegates to its current MemoryStream or
FileStream. Core's `PdfSyntaxReader` owns one 8,192-byte syntax buffer, a logical
long cursor and one captured input length. `GeneratedPdfFile` routes header,
tail, syntax and encoded-range reads through that reader. Payloads still copy
in 81,920-byte blocks with cancellation checks. The reader borrows immutable,
exclusive input and never disposes it. Public APIs, packages, rendering, spool
thresholds and existing syntax limits are unchanged.

## Protocol and retained evidence

- A: baseline production Reader/Core, before either optimization. The saved
  runtime originated at `dfe94b2`; its production algorithms equal main.
- B: Reader delegation only, `c99f878`.
- C: delegation plus Core buffering and tests, `71dff65`.
- All stages use the same benchmark source from `c99f878`. A's saved benchmark
  DLL/PDB were refreshed from B to include the harness's explicit temporary-file
  permission; A's production DLLs were retained. C recompiles the same harness
  source. Actual DLL hashes, rather than just revision labels, identify runs.
- Saved Release runtimes remain in `artifacts/pdf-buffered-read/{A,B,C}` in the
  workspace. [conditions.json](conditions.json) identifies production revisions,
  inputs, font and runtime files. Native runtimes and large generated inputs are
  regenerated instead of checked in.
- Debian 13, .NET SDK 10.0.401, .NET runtime 10.0.12; committed Noto Sans JP regular
  font and isolated Fontconfig. All builds, tests and measurements run serially.
- Three rounds rotate stage order ABC, BCA, CAB. Each process performs two warmups
  and five retained samples. [samples.json](samples.json) retains every warmup,
  sample, input/DLL hash and GC count. [summary.json](summary.json) retains
  round-specific medians and ranges; comparisons must not be read as a general
  percentage guarantee from a pooled median.
- Finalization timing prepares the spool before measurement and writes to a
  nonseekable byte-counting discard stream. No growing output MemoryStream is
  included. It uses fixed deterministic PDFs, not a concurrent render. Whole
  conversion uses the same frozen workbook bytes, font and discard destination
  across stages. Managed allocation includes all managed threads.
- Default 8 MiB, forced 1-byte and (large image only) 64 MiB memory thresholds
  use existing options. Workbook input retains its independent existing policy.
  [generate-fixtures.py](generate-fixtures.py) reproduces four deterministic PDFs
  and the noisy-image workbook from small [retained workbooks](fixtures).
- [diagnostics.json](diagnostics.json) is a separate run with a forwarding input
  wrapper counting the parser's actual spool calls: Read, ReadByte, Position
  gets/sets, Seek, Length gets and bytes returned. It does not claim to count OS
  syscalls inside FileStream. These instrumented timings are excluded from the
  timing comparison. It also records the warmed direct ReadByte allocation loop
  and final output hashes. Whole-conversion `pdfFinalSave.ms` includes Skia Close;
  it is not isolated finalizer time. The only observer installed during timing
  accumulates existing final-save and spill metrics, equally in all stages.

## Reproduction

Check workspace and temporary free space first. To recreate stages in separate
checkouts, use the revisions above, placing these common harness files from
`c99f878` in each checkout before building: Core `Properties/AssemblyInfo.cs`,
Performance `Program.cs`, `PdfReadBenchmark.cs` and its `.csproj`. Keep each stage's
Reader/Core implementation. Restore and build the Performance project in Release
sequentially, then save the complete `bin/Release/net10.0` directory under A/B/C.
A can also be built from main with just those common harness files overlaid.
Keep the supplied source's package lock files and identical font/native files.

From the repository root, with the saved runtimes already built:

```bash
python3 docs/validation/pdf-buffered-read/generate-fixtures.py artifacts/pdf-buffered-read/fixtures
source scripts/test-fonts.sh
python3 docs/validation/pdf-buffered-read/measure.py artifacts/pdf-buffered-read
python3 docs/validation/pdf-buffered-read/compare-output.py artifacts/pdf-buffered-read
```

The output comparison uses PyMuPDF independently of Core and the tests' PdfPig.
Raw output PDFs stay under `artifacts/pdf-buffered-read`; the comparison records
page dimensions, links, extracted text, raw image/mask hashes and page pixel
hashes, excluding timestamp metadata from semantic comparison. Deterministic
handmade finalizations additionally require identical complete file hashes.

A single finalization or whole-conversion command is:

```bash
dotnet artifacts/pdf-buffered-read/C/ExcelRenderer.Performance.dll pdf-read artifacts/pdf-buffered-read/fixtures/objects.pdf 8388608 5
```

Replace the input with `objects.xlsx` for whole conversion; append `--diagnostic`
only in a separate run. The diagnostic writes `<input>.final.pdf` outside timing.
The generated image workbook uses a deterministic 1,800 × 1,800 RGB PNG; the
retained template contains the original small image to keep evidence compact.

## Correctness and required checks

- Baseline: all 2,652 solution tests passed in Release. C: all 2,684 solution
  tests passed after the required default-configuration Rebuild. Both default
  and Release Rebuilds completed with zero warnings and zero errors.
- The 49 focused PDF/spool cases passed. New coverage includes unsigned bytes,
  repeated EOF, mixed Read/ReadByte/Seek/Position, independent OpenRead cursors,
  disposal/temp-file cleanup, warmed allocation loops, buffers of 1/2/3/7/8192
  bytes, every split within representative syntax, short reads of 1–3 bytes,
  numeric-reference lookahead and rewind, in-buffer/no-I/O repeated parses,
  distant seeks and EOF recovery, bounded block-read counts, malformed/truncated
  input, indirect Length, out-of-order object reads, binary syntax-like payloads
  of 8,191–200,000 bytes, copy-end recovery, nonseekable output and cancellation
  between transfer blocks. Existing corruption and unsupported-format checks
  remain active. Structural tests run against memory and spilled spools.
- Core's independent Release coverage gate passed with 368 non-Performance tests:
  line **82.52%**, branch **70.90%**, above the unchanged 82.12% / 70.08% gates.
  Its eight Performance-category cases also passed separately.
- Existing main/Core PDF regressions completed, including independent PdfPig
  page geometry/link assertions, image/mask behavior, Unicode text and drawing
  regressions. Historical fixtures and their hashes were not edited.
- Namespace, removed-PDF-dependency and independent Reader/Core isolation checks
  passed. The only project-reference addition is in the nonpackable Performance
  tool; product package/project distribution settings and NuGet dependencies are
  unchanged.

[Retained logs](logs) record commands' output with whitespace-only padding trimmed; [reader-isolation.json](reader-isolation.json)
retains standalone restore/build/publish/output/AssemblyRef/execution results.
The source validation commands were:

```bash
dotnet restore ExcelRenderer.slnx
dotnet build ExcelRenderer.slnx -t:Rebuild --no-restore --verbosity:minimal -m:1
source scripts/test-fonts.sh
dotnet test ExcelRenderer.slnx --no-restore -m:1
dotnet build ExcelRenderer.slnx -c Release -t:Rebuild --no-restore --verbosity:minimal -m:1
dotnet test tests/ExcelRenderer.Core.Tests/ExcelRenderer.Core.Tests.csproj -c Release --no-build --filter 'Category!=Performance' -p:CollectCoverage=true
dotnet test tests/ExcelRenderer.Core.Tests/ExcelRenderer.Core.Tests.csproj -c Release --no-build --filter 'Category=Performance'
python3 scripts/check-source-namespaces.py
python3 scripts/check-no-pdfsharp.py
python3 scripts/validate-reader-isolation.py
```

## A/B/C results

810 retained uninstrumented samples plus 324 warmups completed. Each time below is the **range of three round medians**, in milliseconds (each round median contains five samples). Exact per-round sample ranges, allocations, final-save metrics, output sizes and spill counts are retained in the JSON files.

### Finalization alone

| PDF / storage | A ms | B ms | C ms |
| --- | ---: | ---: | ---: |
| small / memory | 0.146–0.151 | 0.128–0.129 | 0.110–0.115 |
| small / forced file | 0.271–0.304 | 0.245–0.272 | 0.123–0.139 |
| objects / memory | 27.733–29.756 | 21.785–23.405 | 16.036–17.061 |
| objects / forced file | 89.342–104.549 | 47.495–52.667 | 16.332–17.488 |
| borders / memory | 0.484–0.557 | 0.400–0.506 | 0.328–0.336 |
| borders / forced file | 1.028–1.065 | 0.918–0.980 | 0.444–0.465 |
| image / default file | 1.051–1.147 | 0.998–1.073 | 0.892–0.963 |
| image / forced file | 1.060–1.185 | 1.012–1.092 | 0.866–0.968 |
| image / 64 MiB memory | 0.705–0.756 | 0.643–0.690 | 0.645–0.652 |

Finalization managed allocations below are medians of the retained samples, in bytes. Per-sample allocation variation and round ranges are retained in the raw data and summary.

| PDF / storage | A bytes | B bytes | C bytes |
| --- | ---: | ---: | ---: |
| small / memory | 160,328 | 119,752 | 128,000 |
| small / forced file | 160,328 | 119,752 | 128,000 |
| objects / memory | 23,532,136 | 9,837,704 | 9,845,952 |
| objects / forced file | 23,536,288 | 9,841,824 | 9,850,072 |
| borders / memory | 433,648 | 250,064 | 258,312 |
| borders / forced file | 437,768 | 254,184 | 262,432 |
| image / default file | 194,048 | 138,528 | 146,776 |
| image / forced file | 194,048 | 138,528 | 146,776 |
| image / 64 MiB memory | 189,928 | 134,408 | 142,656 |

B removes the per-byte arrays. C adds **8,248 bytes** relative to B in isolated finalization, accounting for the fixed 8 KiB buffer and additional reader state; this cost is independent of document size. The warmed direct 131,072-byte ReadByte loop (including its EOF read) allocates **4,194,336 bytes in A and 0 in B/C**, for both memory and file storage.

### Complete workbook conversion

| Workbook / PDF storage | A total ms | B total ms | C total ms |
| --- | ---: | ---: | ---: |
| small / memory | 29.956–34.141 | 29.915–31.409 | 30.008–31.194 |
| small / forced file | 31.155–31.370 | 30.507–36.727 | 29.273–32.232 |
| objects / memory | 438.678–483.524 | 355.685–472.538 | 386.227–470.736 |
| objects / forced file | 478.665–514.739 | 331.673–533.457 | 288.721–459.088 |
| borders / memory | 182.135–298.152 | 160.441–255.587 | 220.525–233.580 |
| borders / forced file | 199.804–240.266 | 167.408–235.619 | 156.098–245.569 |
| image / default file | 287.678–296.029 | 285.158–300.470 | 283.063–295.689 |
| image / forced file | 290.284–295.859 | 290.006–299.582 | 283.386–297.925 |
| image / 64 MiB memory | 276.425–300.994 | 286.783–288.587 | 286.564–297.986 |

Existing inclusive `pdfFinalSave.ms` for forced-file workbook conversion:

| Workbook | A ms | B ms | C ms |
| --- | ---: | ---: | ---: |
| small | 8.683–8.782 | 8.436–8.770 | 7.638–8.139 |
| objects | 14.977–19.657 | 17.418–18.796 | 10.264–10.958 |
| borders | 3.768–5.988 | 3.543–5.560 | 1.797–3.449 |
| image | 10.836–11.171 | 10.575–11.259 | 9.225–9.448 |

All stages/thresholds have the same output sizes per input: handmade small 600, objects 175,634, borders 722,835, image 9,720,798 bytes; Skia workbook small 116,302, objects 164,234, borders 25,734, image 9,842,064 bytes. The large image **naturally spills at the default 8 MiB** in both finalization and whole conversion; 64 MiB keeps it in memory. The 1-byte cases deliberately force spilling.

### Operations and output agreement

Representative forced-file object-heavy finalization, counted separately:

| Actual spool operation | A | B | C |
| --- | ---: | ---: | ---: |
| Read | 130 | 130 | 156 |
| ReadByte | 427,951 | 427,951 | 0 |
| Position get | 385,154 | 385,154 | 1 |
| Position set | 269,297 | 269,297 | 137 |
| Seek | 0 | 0 | 0 |
| Length get | 116,381 | 116,381 | 1 |
| Bytes returned | 435,255 | 435,255 | 221,471 |

C replaces byte calls with block reads. Read alone therefore need not fall; the combined Read/ReadByte count falls from 428,081 to 156 in this case. For the 9.72 MB image, the 119 payload blocks dominate: C makes 124 Reads, zero ReadByte calls and five Position sets, versus A/B 122 Reads, 1,735 ReadByte calls and 1,125 Position sets. Fixed read-ahead can slightly increase bytes fetched around a large payload (9,737,689 versus 9,722,816 here); encoded payload copying remains exact.

All nine handmade PDF/storage comparisons have identical complete A/B/C output hashes. All nine Skia workbook/storage comparisons pass independent **PyMuPDF 1.26.6** comparison of every page's dimensions, extracted text, links, raw image/mask bytes and rendered pixels. The mixed workbook has 20 pages, seven links and one image. The other workbook page counts are 3 (small), 18 (borders) and 3 (image). [output-comparison.json](output-comparison.json) records those independent results; timestamp metadata is excluded from semantic comparison.

### Interpretation and limits

The deterministic operation and allocation objectives are satisfied. Isolated finalization improves in these samples, especially with many objects and a file spool. Large image copies already use block transfers and leave less syntax work to improve. Whole conversion includes layout, font preparation, drawing, native encoding, Skia Close and garbage collection; its round ranges overlap and allocations also vary between samples. These results do **not** establish a stable whole-conversion speedup or regression percentage.

Measurements use synthetic retained workbooks, warm input preparation/OS caches and Linux/.NET 10 only. Cold storage, other operating systems/runtimes and customer workloads were not measured. The syntax buffer is constant-sized; existing parsed-object/model memory still scales with document structure. Native render resources and total peak-memory claims are outside this read optimization.

[artifact-hashes.json](artifact-hashes.json) authenticates the retained report, scripts, logs, inputs and measurement files.
