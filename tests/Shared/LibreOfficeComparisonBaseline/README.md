# Reviewed product PDF regression baseline

These 44 PDFs retain the reviewed main/Core outputs for all 22 fixed inputs from
the LibreOffice comparison, after the source-column-metric fix. They are **product
regression baselines, not Microsoft Excel golden files**. Core's unsupported rich
text, conditional formatting, shapes and other intentional differences are retained.
The original LibreOffice references and historical A/B/Core PDFs remain unchanged.

`manifest.json` binds every input, font and expected PDF by SHA-256. The ordinary
Linux PR job generates 22 main PDFs (cached values, CF enabled, explicit fonts)
and 22 Core PDFs (saved values, one font), then compares each with its own baseline:

- Page count, MediaBox, CropBox and rotation.
- Full extracted text from both Poppler and MuPDF, plus glyph IDs, font, size,
  color, positions and bounds from MuPDF text traces.
- Every path, stroke width/color, fill, image payload/placement and link.
- Exact full-page pixels at 144 and 288 DPI with pinned PyMuPDF 1.26.6.

PDF object numbers, creation metadata and serialization bytes are not appearance.
Independent decoded geometry is normalized to 0.001pt, while raster equality stays
exact. No alignment, scaling, crop, difference mask or AA threshold is applied.
The separate border audit checks five sheets and 23 physical strokes per engine
against hand-calculated coordinates/widths/colors, independently of these PDFs.
Mutation checks run the same comparison against deleted text, a deleted border,
extra paint, shifted content, an extra page, and incorrect source Unicode with
unchanged visible glyphs; all must fail.

## Run

After the Release CLI build, Poppler `pdftotext`, and the pinned Python dependencies used by CI:

```bash
source scripts/test-fonts.sh
python3 scripts/check-libreoffice-regression.py
python3 scripts/audit-libreoffice-borders.py --run artifacts/libreoffice-regression --output artifacts/libreoffice-regression/border-audit.json
```

Use a fresh `--run` directory for another generation. `--no-generate` only verifies
the PDFs and per-engine execution records from an existing run; CI generates fresh
outputs. Failures retain a JSON report and expected/actual page images in the run
directory, uploaded by the PR job. LibreOffice is never started by this test.

## Historical Core comparison

The previous Core test failed after the approved metric fix because it still
required old glyph origins within 0.1pt. For example, invoice moved by up to
65.2501pt and grid by 93.9274pt; scaling changed from two pages to one. Those are
layout changes, not antialiasing. Raising that threshold would hide regressions.

The three `historical-core-followups` PDFs separately retain the new outputs of
that harness's exact inputs (its scaling fixture differs from the public sample).
The manifest binds the historical PDF hash, input hash and new PDF hash. Only
these three explicitly named changes use the new complete geometry/pixel contract,
including 300 DPI, plus the existing independent font-outline check. Unknown cases
retain the historical checks. Original PDFs, border contracts, AA reviews and
thresholds are preserved. The ordinary 44-PDF gate tests production defaults too.

## Maintenance

CI has no expectation-recording option. Do not regenerate or replace these PDFs
merely to make a failing build pass. First investigate the failing page/component,
demonstrate the intended behavior independently and review before/after evidence.
Any intentional future baseline change needs explicit input/font hashes, visible
reason and reviewed PDF changes; it must not broaden thresholds or silently omit
cases/engines. A match here establishes preservation of this reviewed Linux output,
not Excel compatibility or Windows pixel equality.

Provenance and the original comparison findings are in
`docs/validation/libreoffice-comparison/core-pagination-fix/README.md`.
