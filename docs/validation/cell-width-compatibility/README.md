# Fractional cell-width compatibility

The wrapped-cell screenshot exposed a width difference, not a saved row-height difference.
Source column A has OOXML width `41.901101`; row 2 has explicit height `120pt`; print scale
is `100%`. The Normal font is Calibri 11pt, while the body uses Noto Sans JP 12pt.

## Implemented behavior

Main adds `ColumnWidthMetricsMode.LibreOfficeCompatible`. Core adds
`PdfExportOptions.ReadOptions.UseLibreOfficeColumnWidths` and an explicit
`MaximumDigitWidthPixels96Dpi` override. Both CLI commands accept:

```sh
excelrenderer render input.xlsx --format pdf --font-file NotoSansJP-Regular.ttf \
  --column-width-metrics libreoffice --column-width-mdw 8.8 -o main.pdf
excelrenderer core input.xlsx --font-file NotoSansJP-Regular.ttf \
  --column-width-metrics libreoffice --column-width-mdw 8.8 -o core.pdf
```

The new mode measures digit advances without pixel hinting, quantizes them to twips
(1/15 pixel at 96 DPI), and converts stored column widths without truncating to whole
pixels. The old source-font and legacy calculations remain the defaults/contracts.
Explicit MDW takes precedence and is not re-rounded. Main can also use `SourceFontFile`
(`--layout-font`) exclusively for layout; Core keeps one drawing font plus a numeric override.

**8.8px is specific to the retained reference's Noto Sans Mono substitution at 11pt.**
It is not the default for every workbook. Without an override, the new mode uses resolved
font metrics (Core uses its supplied regular font), so environments selecting different
fonts can still differ. For reproducible comparisons, supply the reference metric/font.
Neither workbook rows nor print settings are rewritten.

## Measured result

| Output | Body cell width | Body cell height |
| --- | ---: | ---: |
| Main/Core default | 219.750pt / 77.523mm | 120pt |
| Main/Core new mode, explicit MDW 8.8 | 276.547pt / 97.560mm | 120pt |
| Main/Core new mode, automatic Noto Sans JP digit metrics | 255.597pt / 90.169mm | 120pt |
| Frozen LibreOffice reference | 276.463pt / 97.530mm | 120.019pt |

Width error reduces from approximately **20.0mm to 0.030mm** in this fixture.
Main and Core have identical body cell rectangles in both modes. The retained reference
and source workbook hashes are unchanged. The difference in PDF fill edges is below
0.12pt and includes border/export precision; it is not an assertion of full PDF parity.

The new mode leaves the approximately 3.86pt/1.36mm vertical offset relative to this
LibreOffice reference unchanged. Investigation with a header-free copy showed that this
offset originates in header/body positioning, not the explicit row height.
Font weight, line breaks and header/footer handling can still differ. Microsoft Excel
was not run; no claim of exact Excel/LibreOffice parity is made.

[Comparison image](comparison.png) · [PDF measurements](report.json) ·
[Main PDF](main-libreoffice.pdf) · [Core PDF](core-libreoffice.pdf).

## Verification

The new tests cover default/compatible cell width, unchanged saved row height, real font
advance precision and invalid Core numeric overrides. The targeted CLI audit verifies
both products against the frozen reference and checks the original input hash.
PNG/SVG output was also inspected: the SVG background rectangle is 276.54727 × 120pt.
The PNG color region excludes its border/antialiasing pixels and is not used as a
replacement for the PDF's vector dimensions.

Final checks and logs are retained with this report. Historical baselines and thresholds
are not updated for this opt-in mode. Reproduce the dimensional audit with:

```sh
python3 scripts/validate-cell-width-compatibility.py --run artifacts/cell-width-new-run
```

`--cli /path/to/installed/excelrenderer` runs the same checks against a packaged CLI.
For a privately installed .NET runtime, set `DOTNET_ROOT` to its installation directory.
