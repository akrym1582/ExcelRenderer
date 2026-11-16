# Automatic print-boundary clipping (historical)

This directory records the superseded clipping behavior from PR #127. Its PDFs,
manifest and proof tests remain historical evidence. Current output comparisons
use the original references again; these clipped PDFs are no longer active
expectations. Current overflow and one-page fitting are covered by the Core and
main `TextOverflowTests` / `CellTextOverflowTests` suites.

At the recorded revision, horizontal cell-text overflow stops at the resolved print boundary even when
no print area is saved in the workbook. The supplied paper may be wider than the
last used column; that extra space no longer permits cell text to escape the
resolved print range. Ordinary and merged cells use the same shared Core rule.

Historical inputs, reports and PDFs remain unchanged. `manifest.json` binds seven
corrected PDFs to the old PDF hashes and exact current decoded content, paints and
144/300dpi pixels. Appearance inputs are additionally bound by workbook hashes.
The three backend fixtures retain their original reader and page-plan checks;
only the finalized command fingerprints change. Removing the recorded zero-offset
viewports must reproduce every historical command exactly, including text, rich
runs, styles, glyph layout, images and borders.

The independent proof also requires unchanged page boxes, strokes, fills, images,
links, glyph styling and retained glyph positions. A character wholly inside the
horizontal print range cannot disappear. Partial or outside glyphs may be culled
by the PDF canvas; text extractor bounding boxes alone do not describe clipped ink.
The rich-theme, missing-glyph and page-selection fixtures use the corrected PDF
reference for both historical backend comparisons. Native raw/finalized stream
preservation and source-outline validation still run against that checked reference.

The 07-multiple-sheets and cf-phase1 appearance cases use corrected references for
both Main and Core, with the existing exact geometry and 144/288dpi pixel gate.
No dynamic alignment, masks or general raster-threshold changes are introduced.

Run:

```sh
python scripts/test_print_boundary_evidence.py
python scripts/compare-pdf-backends.py --no-build
python scripts/compare-pdf-backends.py --no-generate --reference B --no-exceptions
python scripts/check-libreoffice-regression.py
```

The dedicated tests check all bound references and reject changed viewport widths,
nonzero offsets, missing visible text and additional ink. C# regression tests also
check ordinary/merged cells with explicit/automatic print ranges, non-unit scaling,
and actual PNG/SVG pixels inside and beyond the print edge. Tests for unrelated
font encoding, sheet selection and mapping use sufficiently wide test columns so
they continue to verify their intended behavior within the print range.
