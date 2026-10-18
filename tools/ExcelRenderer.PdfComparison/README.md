# PDF backend comparison

Build the native subset adapter first (CMake and a C++ compiler are required):

```bash
python3 scripts/build-harfbuzz-subset.py
dotnet restore ExcelRenderer.slnx
python3 scripts/compare-pdf-backends.py --fixture all
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
