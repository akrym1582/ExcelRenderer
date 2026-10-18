# PR #90 image decode and PDF storage fixes

This report records the 2026-10-18 review follow-up to
`fdeff40890f7927ed5239b285e9853026dec3728` on `feature/remove-pdfsharp`.
Earlier [removal validation](pdfsharp-removal-validation.md) and
[performance review](rendering-performance-review.md) remain historical evidence.

## Complete pixels, diagnostics and ownership

`SKImage.FromEncodedData` can return an image when only its header is valid.
The shared Core decoder now calls `SKCodec.GetPixels` and requires `Success`;
`IncompleteInput`, `ErrorInInput` and other results are failures. Diagnostics
record the actual codec/decode result, rather than claiming the lazy image was
null. Core and main use the same acquisition path; font collection does not
acquire images or emit image diagnostics.

The deterministic 800×800 PNG fixture retains its header and zlib header, damages
the first IDAT deflate block and recalculates the PNG CRC. Both the lazy image
and codec are created successfully, but pixel decode fails. Before this change,
the regression test failed because no `ImageDecodeFailed` was reported. Main's
normal conversion now reports the warning and keeps later valid images, text
and pages. Strict mode and code-specific error policy throw, call the sink's
abort operation and register no completed artifact. Sheet, page, bounds, byte
count, header and actual reason retain their existing diagnostic contract.
Truncated JPEG/PNG are also rejected after successful codec creation. PNG/SVG
retain their existing diagnostic policy and skip invalid images through the same
decoder.

Successful resources hold immutable decoded pixels plus original JPEG/PNG
encoding when useful. Pixel representation matches the pinned native encoded
image generator, including premultiplied transparency, without an intermediate
`SKColor` roundtrip. All eight EXIF orientations and full transparent pixel
arrays are compared against the native decoder. Native PDF JPEG passthrough
retains the original DCT stream; PNG uses validated pixels with native lossless
PDF encoding. SVG can reuse original JPEG/PNG bytes. No blanket PNG re-encoding
or JPEG quality change is introduced. PDF crops map and clip the full validated
image. This prevents Skia's raster-image subset optimization from changing
interpolation at fractional crop edges; the frozen image fixture's native
content and image streams remain identical. PNG/SVG retain their existing
cubic drawing path.

The conversion-owned 64MiB LRU accounts for decoded pixels and retained encoded
bytes. Oversize entries remain uncached leases. Active leases survive owner
retirement and dispose their native handles on release. Failed attempts cache
only their actual reason under weak byte-array keys, bounded to 512 entries;
call sites still report each distinct image location. Repeated valid images
across collection, drawing and two pages perform one explicit pixel decode
while cached. Eviction or oversize resources may legitimately decode again.
Cancellation is checked around synchronous decode; Skia's individual codec
call itself is not interruptible. OOM/cancellation are not treated as valid
images or ordinary decode success.

## Single-copy structural save

Pinned Skia's linear image sampling does not emit `/Interpolate true` on the
raw fixture. Skipping existing `true` entries alone therefore cannot fix the
review. The finalizer now writes each active object once, applying the same
page/image dictionary corrections, copying each encoded stream directly from
the bounded source spool, and generating a fresh classic xref/trailer. It does
not copy the entire original file before appending image revisions.

Object number/generation, stream length and bytes, Root/Info/ID, Contents,
Resources, ToUnicode/ActualText, subsets, name trees and masks survive the save.
The obsolete `/Prev` chain is removed. Color image interpolation remains true;
SMask interpolation is unchanged. The parser still accepts only the supported
generated format, rejects signatures/encryption/object-stream inputs, and
checks damaged objects/xrefs, cycles and limits. This is not an external PDF
editing API. One 80KiB transfer buffer is reused; payloads are never collected
in a new whole-document buffer or decompressed by the finalizer.

The former original-prefix requirement is replaced with encoded-stream hashes
at the same object identities and whole-output payload occurrence counts. The
existing B/C content/font/metadata gates are retained. Size thresholds, frozen
historical PDFs, reviewed differences and raster goldens are unchanged.

## Reproduction and verification

Run from the repository root after native setup and locked restore:

```bash
source scripts/test-fonts.sh
dotnet build ExcelRenderer.slnx -c Release -t:Rebuild --no-restore --verbosity:minimal -m:1
dotnet test ExcelRenderer.slnx -c Release --no-build --no-restore
python scripts/validate-image-review.py
python scripts/compare-pdf-backends.py --no-build --output artifacts/pr90-image-review/comparison
python scripts/compare-pdf-backends.py --no-generate --reference B --no-exceptions --output artifacts/pr90-image-review/comparison
python scripts/compare-core-removal-baselines.py
```

`ImagePayloadTests` covers RGB, alpha/SMask, JPEG, shared images across three
pages and two different images, including transformed/clipped rendering. It
checks raw/final encoded stream hashes, one payload occurrence in all output
bytes, raw+64KiB size limit and a second finalization's byte idempotence for
already-interpolated fixtures. Independent PyMuPDF verifies these invariants,
JPEG DCT passthrough and the old correctly interpolated RGB reference at 144
and 300dpi. The reference is deliberately not the raw uncorrected PDF.

Low-threshold (1KiB) spool tests cover success, nonseekable caller output,
write failure and cancellation during finalization, checking spill existence,
cleanup and caller stream ownership. Existing tests retain page boxes, links,
Unicode, subsets, escaped strings, line endings and nonzero generations.

Actual final test, comparison, distribution and performance results are recorded
below. Edge/Adobe Reader manual review is not performed
by these automated checks. This work does not merge the PR or publish NuGet.

## Image payload evidence

The review regression failed before the fix: the RGB final PDF was 3,843,122
bytes versus 1,921,925 raw bytes. The encoded color stream occurred twice in
the complete file. The same fixture after structural saving is:

| Fixture | Raw bytes | Final bytes | Final minus raw |
| --- | ---: | ---: | ---: |
| RGB, one page | 1,921,925 | 1,922,041 | 116 |
| Alpha + SMask | 2,534,515 | 2,534,634 | 119 |
| JPEG | 528,823 | 528,939 | 116 |
| Shared RGB, three pages | 1,922,859 | 1,923,125 | 266 |
| Two alpha images, two pages | 5,068,291 | 5,068,514 | 223 |

The RGB payload SHA-256 is
`e5e6507391ab9a871ea00a398e6ff193bd34b32a433c2db1bc788f045931158e`
before and after; its occurrence count changes from two to one. All five
fixtures retain every encoded stream hash and original reference, with one
occurrence per image/mask payload. The RGB PDF is 49.99% smaller than the old
final PDF. The new whole-file byte layout is intentionally different.

The independent CRC check verifies all 238 PNG chunks. The corrupt fixture's
recorded statuses are `Codec=Success`, `Pixels=IncompleteInput`,
`LazyImageCreated=true`. The lazy image alone is therefore insufficient to
validate it. PNG/JPEG repeated across two pages, including crop/rotation/clip,
produce one image object and one explicit pixel decode. The JPEG embedded DCT
bytes equal the original JPEG bytes.

## Executed checks and performance

Implementation: [`eb8bfc0`](https://github.com/akrym1582/ExcelRenderer/commit/eb8bfc0d67097a472ac774045fdf0a8dd04f51a1).
The [validation record](pr90-image-review-data/validation.json) stores input/source
and result hashes, TRX counts/times, SDK/RID and the native binary identity.
[Independent image evidence](pr90-image-review-data/independent.json) retains
per-image payload hashes, filters, sizes, counts and corrected pixel hashes.

Local Linux x64 verification used SDK 10.0.401/runtime 10.0.12, SkiaSharp
4.151.0, HarfBuzz 10.2.0 and pinned independent readers. Native source/hash/export
validation and locked restore succeeded. Release Rebuild reported zero warnings
and zero errors. All **2,089 tests passed, zero skipped**: Core 111, main 668,
Formulas 833, Mapping 172, OpenXml 206 and CLI 99. The 31 new cases include both
original failing reproductions and integrated lifecycle/format/diagnostic checks.

Fresh A→C comparison: 25 fixtures, zero unmet gates, the same 51 individually
reviewed differences. Strict B→C: 25 fixtures, zero differences or exceptions.
Frozen Core comparison: three fixtures/four pages, 166 region/DPI checks using
the existing pinned reviews. Unicode extraction, boxes, links, subsets, crop,
rotation and font/image streams passed. Existing frozen captures, thresholds,
review bounds and PNG/SVG/Markdown goldens were not changed. Namespace,
Core/OpenXml ownership and formula dependency checks also passed.

Four packages were rebuilt and the CLI published locally for inspection;
[the inventory](pr90-image-review-data/package-inventory.json) verifies identical
shared DLL/XML copies. All nine isolated package consumers, including installed
CLI, passed. The removed-dependency audit passed for 22 restored graphs,
788 managed AssemblyRef inventories and all four packages. No package dependency
or public API change is part of this follow-up. Local packages validate Linux
x64 only; CI performs its own OS-specific pack/consumer checks.

For the focused performance check, the unchanged existing harness ran the same
12-page workbook with 32 distinct 256×256 images before and after. Five paired
fresh-process rounds alternate execution order. Each process performs three
warm-up conversions and retains the fourth: **40 total conversions, 10 retained
measurements**. Time/allocation below describe the retained conversion; RSS is
the process high-water mark including warm-up. The harness source is unchanged; the independently built benchmark executables
have separate recorded hashes. Input/product DLL hashes and all samples are retained in
[conditions](pr90-image-review-data/performance-conditions.json),
[raw records](pr90-image-review-data/performance-raw.json) and
[summary](pr90-image-review-data/performance-summary.json).

| Measurement | Before | After | After / before |
| --- | ---: | ---: | ---: |
| Median conversion time | 722.23ms | 811.79ms | 1.124 |
| Median managed allocation | 127.19MiB | 124.84MiB | 0.982 |
| Median peak RSS | 308.61MiB | 290.75MiB | 0.942 |
| Final PDF bytes | 12,314,710 | 6,191,824 | 0.503 |

This sample shows a **12.4% time increase**, 5.8% lower peak RSS and roughly
half the PDF bytes. Complete pixel validation adds work; this correctness fix
is not presented as a general speed improvement. Five samples per build have
substantial timing variation, preserved in the raw data. The new
`imagePixelDecode=32` counter records actual explicit codec pixel decodes per
conversion. The old `imageDecode=32` counter measured resource creation, so it
cannot establish the old native pixel-decode count. The two-page repeated-image
tests separately prove one explicit decode for each cached PNG/JPEG, including
fractional crop, rotation and clipping.

An initial performance pass overlapped the native adapter rebuild. It is
retained as [build-overlap raw data](pr90-image-review-data/build-overlap-raw.json)
and [summary](pr90-image-review-data/build-overlap-summary.json), and is not used
for the table. The authoritative paired run started after native build/restore,
all tests/comparisons/consumer builds and compiler-server shutdown completed.
The rebuilt adapter's hash matches both benchmark copies. The image workload
was not changed and no slow retained samples were removed.

Reproduce that focused measurement with existing fixture bytes and saved
pre-fix binaries:

```bash
python tools/ExcelRenderer.Performance/review-performance.py \
  --before /path/to/pre-fix/ExcelRenderer.Performance.dll \
  --after tools/ExcelRenderer.Performance/bin/Release/net10.0/ExcelRenderer.Performance.dll \
  --inputs artifacts/pdf-removal-benchmark \
  --output artifacts/pr90-image-review/measurement --only pdf-images
```

These measurements cover this image workload, not a rerun of the earlier full
performance matrix. Skia still retains document resources until native close;
cache estimates and a bounded spool do not promise constant process memory.

## CI and remaining manual work

[Implementation CI run 130](https://github.com/akrym1582/ExcelRenderer/actions/runs/39125786270)
for `eb8bfc0` completed with **all seven jobs successful**
([recorded job identities/results](pr90-image-review-data/ci.json)). Linux runs the full
2,089-test suite, unchanged-threshold A→C/B→C/Core comparisons and the new
independent image payload/pixel gate. Linux and Windows each pass Core's 111
tests; the native distribution jobs each pass the 139 selected main tests
(including all 22 image-review cases), CLI's 99 tests, native build, four-package
checks and nine isolated consumers. Formula jobs pass on both OSes as well.
The following documentation/evidence commit does not change product code and
triggers another normal CI run.

Core's supported single-font/PDF-only scope, public stream ownership and the
existing diagnostic policy remain unchanged. Edge/Adobe Reader manual checks
and a complete multi-RID release inventory are not established by these runs.
The PR remains draft; no merge or NuGet publication is performed.
