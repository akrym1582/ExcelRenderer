# LibreOffice provisional PDF references

These PDFs were exported from the repository's existing XLSX fixtures on 2026-10-22 (JST). They are **LibreOffice comparisons, not Microsoft Excel golden files**. Windows Excel export and review remain future work. No ExcelRenderer output was substituted for the reference files.

Start the next verification task with [the Japanese Codex instructions](Codex_Instructions.ja.md). The instructions cover generation of both main and Core PDFs with the latest product code, three-way page/text/image comparison (LibreOffice/main, LibreOffice/Core, main/Core), Core scope differences, diagnosis, justified fixes and regression checks.

## Provenance

- Exporter: LibreOfficeDev 26.8.0.0.alpha0, build `2c87e51eeaa2b413ff4ae097b2705eea1995d8e5` (development build).
- The exact input commit, XLSX/PDF SHA-256 values, workbook sheet metadata, requested font families, actual available font files, page sizes and conversion stdout/stderr are in [manifest.json](manifest.json).
- Whole-workbook export uses the original print settings and `calc_pdf_Export`. No print scaling override, single-page export, workbook save or input rewrite was performed. Hidden-sheet behavior follows Calc defaults; sheet state is retained in the manifest.
- Fontconfig is restricted to committed Noto Sans JP, IPAmjMincho and the test font directory (including synthetic font fixtures). Font file hashes are recorded. Requested fonts such as Calibri/Meiryo may be substituted; synthetic bold/italic and fallback choices can differ between renderers.
- Locale: `C.UTF-8`; timezone: UTC. Calc may recalculate formulas while loading. These PDF exports are not numerical formula oracles.
- Calc can interpret conditional formatting, x14 extensions, icons, drawings, print pagination and font metrics differently from Excel. Preserve visible discrepancies as evidence; do not repair a reference to make it match the product.

## Regeneration

Requires Python 3 with PyMuPDF, LibreOffice (`soffice`) and Fontconfig (`fc-cache`, `fc-list`). Docker is not required. From the repository root:

```bash
python3 docs/validation/libreoffice-reference/generate.py
```

This explicitly replaces reference PDFs and the manifest; do not run it during ordinary regression checks or CI. Review changed versions, fonts, inputs and hashes before accepting replacement references. PDF metadata can make byte hashes differ even when appearance is unchanged. Refresh `artifact-hashes.json` after intentional retained-file changes.

All generated PDF pages were rasterized to check readability by the rasterizer and visually reviewed as contact sheets. This is export QA, not an ExcelRenderer comparison or proof of Excel fidelity. No product code was changed in this reference capture.

## Cases

The table below is generated from the retained manifest. Paths are relative to the repository root for inputs, and this directory for PDFs.

| Case | Input | PDF | Pages |
|---|---|---|---:|
| 01-japanese | `tests/ExcelRenderer.Tests/SampleInputs/01-japanese.xlsx` | [01-japanese](pdf/01-japanese.pdf) | 1 |
| 02-image | `tests/ExcelRenderer.Tests/SampleInputs/02-image.xlsx` | [02-image](pdf/02-image.pdf) | 1 |
| 03-wrapped-text | `tests/ExcelRenderer.Tests/SampleInputs/03-wrapped-text.xlsx` | [03-wrapped-text](pdf/03-wrapped-text.pdf) | 1 |
| 04-text-decoration | `tests/ExcelRenderer.Tests/SampleInputs/04-text-decoration.xlsx` | [04-text-decoration](pdf/04-text-decoration.pdf) | 1 |
| 05-borders | `tests/ExcelRenderer.Tests/SampleInputs/05-borders.xlsx` | [05-borders](pdf/05-borders.pdf) | 2 |
| 06-layout-and-pagination | `tests/ExcelRenderer.Tests/SampleInputs/06-layout-and-pagination.xlsx` | [06-layout-and-pagination](pdf/06-layout-and-pagination.pdf) | 4 |
| 07-multiple-sheets | `tests/ExcelRenderer.Tests/SampleInputs/07-multiple-sheets.xlsx` | [07-multiple-sheets](pdf/07-multiple-sheets.pdf) | 2 |
| 08-print-scaling | `tests/ExcelRenderer.Tests/SampleInputs/08-print-scaling.xlsx` | [08-print-scaling](pdf/08-print-scaling.pdf) | 1 |
| 09-cell-border | `tests/ExcelRenderer.Tests/SampleInputs/09-cell-border.xlsx` | [09-cell-border](pdf/09-cell-border.pdf) | 2 |
| cell-argb | `tests/ExcelRenderer.Tests/SampleInputs/CellArgb/10-cell-argb.xlsx` | [cell-argb](pdf/cell-argb.pdf) | 1 |
| border-conflicts | `tests/ExcelRenderer.Tests/SampleInputs/BorderResolution/border-conflicts.xlsx` | [border-conflicts](pdf/border-conflicts.pdf) | 5 |
| richtext | `tests/ExcelRenderer.Tests/SampleInputs/ReaderRichText/representative.xlsx` | [richtext](pdf/richtext.pdf) | 1 |
| cf-phase1 | `docs/validation/conditional-formatting/representative.xlsx` | [cf-phase1](pdf/cf-phase1.pdf) | 1 |
| cf-phase2 | `docs/validation/conditional-formatting-phase2/representative.xlsx` | [cf-phase2](pdf/cf-phase2.pdf) | 1 |
| cf-mapped-scale | `docs/validation/conditional-formatting-phase2/mapped-scale.xlsx` | [cf-mapped-scale](pdf/cf-mapped-scale.pdf) | 1 |
| cf-phase3-icons | `docs/validation/conditional-formatting-phase3/fixtures/icons.xlsx` | [cf-phase3-icons](pdf/cf-phase3-icons.pdf) | 1 |
| cf-phase3-layout-mapping | `docs/validation/conditional-formatting-phase3/fixtures/layout-mapping.xlsx` | [cf-phase3-layout-mapping](pdf/cf-phase3-layout-mapping.pdf) | 1 |
| cf-phase3-legacy-bars | `docs/validation/conditional-formatting-phase3/fixtures/legacy-bars.xlsx` | [cf-phase3-legacy-bars](pdf/cf-phase3-legacy-bars.pdf) | 1 |
| cf-phase3-x14-bars | `docs/validation/conditional-formatting-phase3/fixtures/x14-bars.xlsx` | [cf-phase3-x14-bars](pdf/cf-phase3-x14-bars.pdf) | 1 |
| shapes | `docs/validation/render-finalization-performance/inputs/shapes.xlsx` | [shapes](pdf/shapes.pdf) | 1 |
| actual-shape-text | `docs/validation/render-finalization-performance/inputs/actual-shape-text.xlsx` | [actual-shape-text](pdf/actual-shape-text.pdf) | 1 |
| japanese-grid-report | `samples/excel/japanese-grid-report.xlsx` | [japanese-grid-report](pdf/japanese-grid-report.pdf) | 1 |

Total: 22 workbooks, 32 pages.

## Observations from export QA

- `05-borders` and `09-cell-border` span two pages; `06-layout-and-pagination` spans four. These are retained Calc results, not asserted expected Excel page counts.
- `border-conflicts` contains sparse line-only sheets; its first four pages have no extractable text. All five pages were rendered and inspected.
- `shapes` exports only the `anchor` text in this environment; do not use absence of the expected drawing as a product expectation. Inspect the source drawing and classify this in the follow-up. `actual-shape-text` provides an additional visible drawing/text case.
- Legacy data bars are rendered with gradients by Calc. Pixel equality to the product's intentionally solid-bar implementation is not required.
