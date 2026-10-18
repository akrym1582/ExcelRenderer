# Frozen A/B/C PDF comparison

The final solution renders only C (new measurement + Skia). A (old measurement + old renderer) and B (old measurement + Skia) are immutable PDFs captured at `71deba48ea4a397641c9ad155bc1c8b3ddf8b5d9` under `tests/Shared/PdfRemovalBaseline`. Normal build/test/CI never restores or executes the removed dependency.

Build the native subset adapter first (CMake and a C++ compiler are required):

```bash
python3 scripts/build-harfbuzz-subset.py
dotnet restore ExcelRenderer.slnx
python3 scripts/compare-pdf-backends.py --fixture all
python3 scripts/compare-pdf-backends.py --no-generate --reference B --no-exceptions
```

Validation uses PyMuPDF 1.26.6, numpy 2.3.5, Pillow 12.3.0, fonttools 4.61.1,
scipy 1.17.0 and Poppler's `pdftotext`. These tools are not product runtime dependencies.
Install the Python packages in an isolated validation environment. The script never
rescales, aligns, or crops the raster pair to hide differences.

`--fixture commands`, `--fixture links`, `--fixture invoice` and `--fixture japanese-grid-report`
select individual cases; `--output PATH` chooses the artifact directory.
`--no-generate` rechecks existing PDFs. A nonzero exit code means an unmet gate;
see `differences.json` and `index.html`, plus the complete difference/overlay PNGs.
The RGB/ink thresholds are fixed in the plan and are not relaxed automatically.
Release is the default; `--configuration Debug` selects Debug, and `--no-build`
uses an already built harness. `--no-exceptions` displays raw screening failures.

The named `styles`, `borders`, `images`, `shapes`, `transforms`, and `blank` scenes
cover individual feature regions. Retained and SDK-generated XLSX fixtures cover
selection, trim, rich text, headers, links, mixed sizes, missing glyphs and fixed
timestamps. The complete suite currently contains 25 fixtures.

`reviewed-differences.json` limits accepted AA and independent-oracle improvements
by fixture/page/region/dpi, command hash and individual measured bounds. It never
auto-updates. `scripts/pdf_geometry_evidence.py` verifies source font outlines,
glyph/path positions, vector paints, original CBDT glyphs, ellipse equations and
source crop samples. Failed structural evidence invalidates an exception.

```bash
python3 scripts/check-pdf-comparison-mutations.py --baseline artifacts/pdf-comparison
```

The mutation check removes IVS glyphs and adds vector ink to prove those defects
still fail when reviewed exceptions exist. Full diff/overlay images remain in
the report; no page masks or raster alignment are applied.

The `commands` path feeds the exact same finalized commands and physical fonts to
both renderers. It covers A4/A3/Letter/fractional point sizes, ordinary Japanese,
Kangxi radicals, supplementary characters, IVS, emoji, combining marks, repeated
spaces, lines and fills. Expected Unicode is independent of PDFsharp extraction.
A full-font comparison-only PDF proves subset size reduction and exact Skia
raster/text preservation. The raw Skia PDF is retained to inspect structural
finalization separately. Full embedding is not a public API or production fallback.

The XLSX path uses the public RenderAsync API for each engine and compares
page descriptors, diagnostic results and finalized command fingerprints. Fingerprints
are checked across Skia's collection/drawing passes as well. This instrumentation
is internal and disabled in ordinary conversions; the manifest schema is unchanged.

See `docs/skia-pdf-validation.md` for actual results, platform scope and manual viewer steps.

The unchanged first-stage reviewed list applies only to A→C. B→C must pass without
exceptions. `pdf_native_evidence.py` checks frozen B hashes, byte-identical drawing
content and embedded streams, native C encoded-stream/object-identity preservation,
single-copy image payloads, and bounded numeric metadata precision. The old save path's implicit RGB page transparency group
is explicitly retained; otherwise byte-identical content still rasterizes differently.
No golden/list update is used to accept that compositing difference.

`metrics.json` and command JSONL retain old metric/wrap/run/baseline/geometry data.
The same generation exports current data for exact logical comparisons. Raw C PDFs
and all A/B/C differences/overlays are validation artifacts, not product assets.

Manual baseline reproduction uses a separate checkout of the recorded first-stage SHA,
its pinned fonts/native recipe and fixed clock; see the recorded font hashes and
`tests/Shared/PdfRemovalBaseline/manifest.json`. Keep that checkout outside the final
solution. Do not overwrite the committed baseline with outputs from C. Large full-font
PDFs are excluded from the fixed baseline; the retained PDFs/JSON are about 3.6MB.

The harness also supports `--api-diff <old-assembly> <new-assembly>` and
`--audit-assemblies <json-path-list>` using PE metadata without loading old code.
