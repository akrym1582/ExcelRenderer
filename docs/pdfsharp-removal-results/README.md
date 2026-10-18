# Recorded removal evidence

These compact files accompany [the validation report](../pdfsharp-removal-validation.md).
Frozen original PDFs, source projections and metric samples live in
`tests/Shared/PdfRemovalBaseline`; their manifest hashes are checked before comparisons.

- `A-C.json` / `A-C-differences.json`: final user-facing comparison, retaining the
  first-stage 51 reviewed drawing differences with no new broad exception.
- `B-C.json` / `B-C-differences.json`: measurement/writer comparison without exceptions.
- `core-comparison.json`: independent text, glyph origins, outlines and regional
  144/300dpi evidence for three frozen Core fixtures (four pages).
- `core-antialiasing-review.png`: old / new / difference crops for the eleven
  exactly bounded Core renderer antialiasing differences. These preview crops
  are scaled for reading; comparison metrics use original pixels without resizing.
- `core-raster.json`: 17 unchanged historical Core golden pages and current full
  reference comparisons; one exact-pixel print-scaling review is explained in the report.
- `api-diff.json` / `core-api-diff.json`: assembly metadata signatures, without
  loading the old product runtime.
- `font-formats.json`: independently extracted Unicode from the six format probes.

Full PDFs, native-prefix captures, per-region PNGs and interactive HTML reports
are reproducible under `artifacts/pdfsharp-removal-current` and uploaded by CI.
Large full-font comparison intermediates are deliberately not committed.
