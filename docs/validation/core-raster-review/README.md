# Core scaling raster review

PR #99's Linux Core job failed on `08-print-scaling-core-2.png`.
The frozen raster differs from the current output by MAE 0.280540, above
the unchanged 0.25 review threshold. This page already had an exact
pixel-hash review for horizontal-edge antialiasing. Shared-border resolution
changed that hash, so the old review correctly rejected the new output.

Rendering pre-border main `513ddd4` with the same font, input and Poppler
reproduces the old reviewed pixel hash exactly. Compared with that output,
the new renderer changes **26 pixels** (MAE 0.002196): seven pixels at each
top/bottom corner row and one at each of twelve right-edge row endpoints.
These are antialiasing effects of deduplicating and joining border strokes,
not missing lines or moved content.

![Pre-border, shared-border, and amplified difference](edge-comparison.png)

The panels show a nearest-neighbor enlarged top-right crop: pre-border
main, PR output, and the difference amplified eight times. Black means
unchanged in the difference panel.

`verify.py` independently checks that axial interval coverage and stroke
styles agree to 0.001pt after unioning duplicate/subdivided segments.
Non-border paints, glyph IDs/origins and images pass the existing independent
checks. The PDF paint count drops from 371 to 29 as intended by shared-edge
resolution. Decoded content, paint geometry and pixels at both 144 and
300 DPI match the **existing** `core/scaling/2` shared-border contract;
that contract was not regenerated. Core and current full-reference pixels
at 96 DPI are identical. Numeric results are in `evidence.json`.

The fix updates only this page's reviewed pixel hash and explanation.
The original golden PNG, its hash, captured input hash, all thresholds,
and the exact-match requirement remain unchanged. Other pixel changes
cannot use this review.

To reproduce, build `tools/ExcelRenderer.Core.Validation` at pre-border
main `513ddd4` and render the captured
`tests/ExcelRenderer.Core.Tests/Baselines/Inputs/08-print-scaling.xlsx`
with `third_party/NotoSansJP/NotoSansJP-Regular.ttf`:

```sh
dotnet /path/to/pre-border/ExcelRenderer.Core.Validation.dll render \
  tests/ExcelRenderer.Core.Tests/Baselines/Inputs/08-print-scaling.xlsx \
  /tmp/pre-border.pdf third_party/NotoSansJP/NotoSansJP-Regular.ttf 1
python3 scripts/validate-core.py --visual-only
python3 docs/validation/core-raster-review/verify.py /tmp/pre-border.pdf
```

The review script additionally needs PyMuPDF 1.26.6 and Pillow. It checks
the existing PDF contracts and writes the evidence and comparison crop;
it does not accept or modify any baseline.
