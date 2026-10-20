# Direct PostScript output

The main `ExcelRenderer` assembly writes one multipage `.ps` document directly from
finalized drawing commands. PDF/SVG conversion, full-page images, text outlines and
Ghostscript are not part of the product output path. Core exposes no PostScript API.

## Usage and compatibility

```csharp
await ExcelConverter.ConvertToPostScriptAsync("input.xlsx", "report.ps",
    new PostScriptExportOptions { FontOptions = new() { AllowSystemFonts = false } });

using var input = File.OpenRead("input.xlsx");
using var output = File.Create("selected.ps");
var result = await ExcelConverter.RenderAsync(input, new RenderRequest
{
    OutputFormat = OutputFormat.PostScript,
    Selection = new() { Pages = [1, 3] },
    Buffering = new() { MemoryThresholdBytes = 8 * 1024 * 1024 },
}, new SingleStreamOutputSink(output));
```

Low-level callers use `ExcelRenderer.PostScript.PostScriptRenderer` with the same
`DrawCommand` and font manager used by layout. Its buffering/cancellation overload
retains caller stream ownership. A high-level conversion permits mixed page sizes;
the low-level `IRenderer` overload uses its supplied common size. An empty low-level
command list produces one blank page. The selected high-level document's page count
is preserved, including empty sheets. No seek on the destination is needed.

`ps` follows the existing new-file rule; `render --format ps` uses the existing
`FileOutputSink` replacement behavior. Directory sinks produce `workbook.ps`, MIME
`application/postscript`, kind `ps`. The manifest has one document artifact and
individual selected page descriptors. Selection, order, ranges, trimming, formula
policies and diagnostics use the existing pipeline. Continuous layout is rejected.
Hyperlinks retain appearance only. There is no EPS, printer transport, print UI or
transparency fallback.

The document declares DSC 3.0, clean ASCII data and LanguageLevel 3. It uses Type 42
fonts, requiring a TrueType rasterizer; large retained tables are split into strings
and therefore require **PostScript interpreter 3011 or later** (or a compatible
implementation such as the tested Ghostscript). LanguageLevel 2 compatibility is
not claimed. Type 42 is standard in LanguageLevel 3, but the large-table extension
is a separate version requirement. Actual printers have not been tested.

## Fonts, geometry and failures

Each physical immutable font snapshot is collected once across the whole document.
The existing Core TTC face extractor, TrueType validator, composite glyph closure
and subsetter are reused. The subsetter preserves original GIDs: unused outlines
are removed, `loca` and checksums are rebuilt, and the old-GID to embedded-GID map is
identity. This avoids introducing another remapping/subsetting algorithm.

Each Type 42 base resource contains one `sfnts` array. Encoding-bank fonts borrow
that array and define their own Encoding and CharStrings, with at most 256 codes.
Banks also separate different GIDs for the same Unicode glyph name (for example,
base and variation glyphs). BMP names are `uniXXXX`, supplementary names `uXXXXX`.
No anonymous names or invisible duplicate text are used to satisfy extraction.
Chunks contain at most 65,534 sfnt bytes and a trailing ignored zero byte. A glyf
outline is not split across strings. The static font matrix is identity; bounds
are derived from `head` in em units.

Text consumes finalized font size, run offsets, baselines, glyphs and per-glyph
advances. Missing compatibility/shape layouts are finalized through
`Drawing/CommandTextFinalizer`, shared with PDF. Already finalized text is not
rewrapped, reshrunk or resolved again. Point coordinates have downward Y; one page
transform establishes that system and a negative Y font scale keeps glyphs upright.
Images use RGB samples, ASCIIHexDecode and an image dictionary, with crop, flips,
rotation and clipping in point coordinates. Shape paths use cubic curves; stored
quadratic paths are converted exactly. Nested viewports balance `gsave/grestore`.
Border geometry reuses Core through the existing border adapter. No new Core
output responsibility is introduced.

| Diagnostic | Behavior |
| --- | --- |
| `PostScriptFontUnsupported` | CFF/CFF2, variable, color, embedding-restricted, subset-prohibited or oversized single-glyph fonts fail. No replacement/outline fallback. |
| `PostScriptTransparencyUnsupported` | Color alpha between 0 and 255 or image pixels with intermediate alpha fail. |
| `PostScriptImageTransparencyUnsupported` | Mixed zero/255 image alpha fails; binary masks are not supported. |
| `PostScriptImageDecodeFailed` | A corrupt/incomplete image fails rather than being silently omitted. |
| `PostScriptCommandUnsupported` | Unknown command types fail. |

Fully transparent drawing elements/images can be omitted. Alpha is never flattened
onto white or silently discarded. Font eligibility is checked by the existing
policy: this implementation diagnoses subsetting-prohibited fonts rather than
adding a new full-embedding exception. Failures abort the sink artifact. The bounded
spool is disposed on failure/cancellation; destination streams remain caller-owned.
Fonts, native image resources and page payloads have separate working sets from
the intermediate spool's configurable memory threshold.

## Unicode preservation and decoder

Three independent properties are tested:

1. Visible text uses embedded fonts and `show` operators; it is not outlined or
   converted to image samples.
2. Original Unicode, including IVS and supplementary characters, is preserved in
   nonexecuting application comments. This is lossless source evidence, not a
   standard DSC field or a promise of viewer search/copy behavior.
3. Ghostscript `txtwrite` extracts standard Latin, symbols, kana and kanji, including
   more than 256 distinct characters. A simple single line must match exactly
   after the device's leading margin and terminal line ending are removed. Wrapped
   bank coverage ignores device-generated line wrapping/indentation.

IVS rendering is compared with direct PNG using the same selected glyph IDs.
`extraction.json` records whether full IVS sequences and supplementary characters
were externally extracted by the installed Ghostscript. Do not infer those
properties from a successful metadata round trip: arbitrary external IVS and
supplementary extraction, rotation reading order and PDF-conversion preservation
are not guaranteed. Text order in complex page layouts is viewer/device dependent.

Metadata line grammar (not DSC):

```text
%ExcelRenderer-Text-v1: RECORD_ID PART/PART_COUNT BASE64
```

IDs increase within a document. Parts start at 1, contain at most 160 Base64
characters and must all be present exactly once. Concatenate in part order before
Base64 decoding. All fields use little-endian `.NET BinaryWriter` encoding:

| Field | Encoding |
| --- | --- |
| Kind | byte: 0 = original command, 1 = resolved run |
| Page, command ID | int32, output page and page-local text command ID |
| Original text | UTF-8 string; byte count encoded as unsigned 7-bit continuation integer |
| Run-only font identity | UTF-8 string; immutable original font content identity |
| Run-only X, baseline, size | three IEEE 754 float64 values, points |
| Run-only glyph count | int32 |
| Each glyph | bank name (string), output code (byte), original/embedded GID (uint16), source UTF-8 cluster offset (uint32), advance at one point (float64) |

Run coordinates are in the command's current point coordinate system, before
enclosing rotation/viewport transforms; the mapping is not a separate page geometry
interchange format.

Original command records retain explicit newlines/whitespace, even when lines are
wrapped or vertical compatibility text is normalized. Run records retain source
sequences separately from displayed replacement glyphs. The font identity is the
original physical snapshot identity, not a hash of the subset.

```bash
python3 scripts/decode-postscript-text.py output.ps --output text.json
python3 scripts/validate-postscript.py output.ps --output font-checks.json
```

The independent validator reconstructs sfnt bytes, checks every table and whole-font
checksum, verifies bank/code/CharStrings/GID correspondence against metadata, checks
actual visible `show` order and reconciles DSC pages with `showpage`. It does not
call Ghostscript and is not a substitute for execution or appearance validation.

## Development and CI verification

Ghostscript is an independently installed, unmodified development tool. No binary,
library, prolog or implementation code from Ghostscript is shipped. The renderer
was authored against the public Adobe specification; the project remains MIT.
Ghostscript is AGPL/commercial dual licensed. A future product/runtime integration
would require separate license consideration.

CI installs Ghostscript, logs its version and sets
`EXCELRENDERER_REQUIRE_GHOSTSCRIPT=1`, so missing Ghostscript fails instead of
silently skipping. Local Ghostscript facts may skip with an explicit reason if no
`gs`, `gswin64c.exe` or `gswin32c.exe` is on PATH. Processes use argument lists,
`-dSAFER -dBATCH -dNOPAUSE -dNOPLATFONTS`, concurrent stdout/stderr draining, a 60-second timeout
and process-tree termination. Nonzero exit, warnings, errors and substitution
messages fail validation. Raw stdout/stderr are saved as `PostScript/ghostscript-*.log`
in the test artifacts. One compatibility exception applies only to a successful
`txtwrite` invocation whose banner identifies Ghostscript 10.02.1: a single exact
`Error: finalizing subclassing device while child refcount > 1` line is a device
cleanup defect, reproducible with standalone Helvetica `(ABC) show showpage`.
The output and all other diagnostics are still checked; another occurrence,
another version/device or a nonzero exit fails. Ghostscript 10.05.1 does not emit
this diagnostic. See the [CI follow-up record](postscript-validation.md#ci-ghostscript-10021-の互換性修正).

The 300 DPI PNG comparison uses the same finalized commands for direct PNG and PS.
Dimensions are exact for these fixed comparison canvases. For arbitrary paper
sizes, Ghostscript rounds the device pixel dimensions whereas direct PNG uses a
ceiling; a default raster can therefore differ by one pixel. Paper sizes in points
are checked separately, including mixed A4/A3 pages. Dark ink and colored regions are compared in both directions
in 64-pixel tiles. Up to four neighboring pixels accommodate observed native PNG
advance rounding (notably spaces) and independent font rasterization; dark-ink
threshold 150 and RGB distance 100 suppress antialiasing differences. A tile fails
when more than 15 pixels and 12% of its compared ink are unmatched. These tolerances
were measured on the committed fixture, and removal of one Japanese glyph, a
5-point text displacement and line deletion must all fail using the same settings.
Small discrepancies cannot disappear into a whole-page average. Image interpolation
is advisory in PostScript and differs from Skia's cubic sampler; the fixture tests
crop/rotation/flip with enough source pixels to distinguish geometry from filter
choice. Baseline, PS raster, absolute difference, mutation images and numeric
results are uploaded with the test artifacts.

An original geometric test TTC face is also rendered after its source copy is
deleted, with platform fonts disabled and substitution warnings rejected, and
compared with direct PNG. It cannot pass by substituting a standard font.

The fixture also covers Japanese bold/italic selection, underline, fallback, IVS,
rotated text, wrapping, shrink-to-fit, partial fonts/colors, borders, shapes and
shape text, nested clips, image transforms and mixed A4/A3 paper. Unit/integration
tests additionally cover TTC selection, composite structure, long sfnts, name/bank
collisions, original-text chunking, unsafe strings, nonseekable streams, unsupported
fonts/alpha, cancellation, sink aborts and temporary-file cleanup.

`performance.json` records three repetitions each for a Japanese border grid,
repeated text over ten pages, images over four pages, and 350 distinct glyphs:
generation time, PS size, font counts/bytes, spool peak capacity/spills, process peak
working set and Ghostscript execution time. Font-manager setup can be warm across
repetitions; peak working set is the test process's cumulative high-water mark,
not isolated conversion memory. `measurements.json` also records a full two-sheet
XLSX conversion. No absolute performance acceptance threshold is invented.

See [the recorded implementation validation](postscript-validation.md) for the
actual commands, environment, results and untested areas.

## Specifications

- [Adobe Type 42 Font Format, technical note 5012, 31 July 1998](https://adobe-type-tools.github.io/font-tech-notes/pdfs/5012.Type42_Spec.pdf), especially sections 2.3 and 4.2. Consulted from the Adobe repository during implementation on 2026-10-20.
- [Ghostscript output devices](https://ghostscript.readthedocs.io/en/latest/Devices.html) and [vector/text devices](https://ghostscript.readthedocs.io/en/latest/VectorDevices.html).
- [Ghostscript license information](https://artifex.com/licensing).
