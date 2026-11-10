# PR 113: invoice and pivot display follow-up

This follow-up addresses the two screenshots raised after commit `548b559`.
It does **not** establish Microsoft Excel parity or implement pivot aggregate generation.
The earlier [review](../pr113-review/README.md), PDFs, inputs and hashes remain unchanged.

## Invoice width and formatting

The source Normal style is Calibri 11pt. In the original isolated Fontconfig environment,
`fc-match Calibri` resolves to **Noto Sans Mono**, while main/Core use their diagnosed
7px source-column compatibility metric and draw with Noto Sans JP.
The resulting LibreOffice reference is not using the same column metrics.

| Title fill width | Points |
| --- | ---: |
| Main / Core, unchanged source-column policy | 456.750 |
| Original LibreOffice reference, Mono substitution | 556.795 |
| Additional LibreOffice reference, explicit Carlito substitution | 482.712 |

The Carlito experiment reduces the width difference from 21.9% to 5.7%, but does not
eliminate it. The lower section starts at y=466.600pt in main/Core, y=449.754pt in the
original reference, and y=463.040pt with Carlito. Source XLSX files and print settings
were not changed. No global MDW override, tolerance relaxation or replacement of the
original reference was made. Missing Calibri, different column rounding, font weights
and other application behavior remain limitations. Original source-font metrics should
be supplied when exact source geometry is required; matching LibreOffice substitution
is not proof of Excel correctness.

The screenshot also exposed a genuine bug: number-format `[Red]` was discarded.
Reader now selects the color from the applicable numeric/text format section, including
numeric conditions; Core applies it after base-style caching and before main conditional
formatting. The eight named colors are supported; indexed `[ColorN]` is not implemented.
Positive/negative/zero values sharing one style do not leak their selected colors.
The invoice's two negative amounts and parenthesized negative number now render red.

[Invoice comparison](invoice/contact.png) · [geometry](invoice-widths.json) ·
[remaining text/position differences](invoice/comparisons.json).

Carlito was retrieved from
`https://raw.githubusercontent.com/google/fonts/main/ofl/carlito/Carlito-Regular.ttf`.
Its SHA-256 is recorded in [execution.json](execution.json); its [OFL](Carlito-OFL.txt)
is retained. This font is used only for the additional LibreOffice experiment, not added
to the product or its runtime dependencies.

## Pivot display

The synthetic pivot fixture has source cells and pivot definitions/cache records but
**no saved aggregate cells** at F4/K4. LibreOffice generates those cells on import;
main/Core previously emitted only source data without notifying the missing tables.
Updating cache records and setting refresh flags does not generate displayed aggregates.

- Main/Core now reject a pivot rectangle without any nonempty saved output inside an
  explicit selected/print region before writing PDF bytes: `PivotSavedValuesMissing`.
  Main also records the error diagnostic; Core throws `InvalidDataException`.
- Pivots outside explicit print areas do not block rendering source data.
- Saved output is rendered. Cache `refreshOnLoad`/`invalid` flags produce
  `PivotRefreshRequired`, because saved totals may be stale.
- Detection does not prove partially populated tables are complete or current.
  The reader streams location/root flags without loading entire cache dictionaries.
- Mapping still preserves unknown parts and updates supported cache records as verified
  in the prior change. It does not update aggregate output cells.

[Reference-only refreshed copy](pivot-refreshed-reference/after.xlsx) and its
[three-engine comparison](pivot-refreshed-reference/contact.png) demonstrate that both
saved summary tables render. All three engines produce one page in the controlled font
environment. LibreOffice refresh-on-open still changes headers and truncates some labels;
[those differences are recorded](pivot-refreshed-reference/comparisons.json).
This copy is **not** a production Mapping result: LibreOffice resaving may discard unknown
parts or reinterpret pivot filters. Original files were hash-checked unchanged.

Automatic pivot aggregate generation remains unimplemented. It needs an explicit contract
for grouping, row/column/page filters, subtotals, ordering, styles, output-cell ownership
and collision handling before it can safely populate the worksheet.

## Preserved comparison contracts

All original Reader golden files, stage A/B/Core PDFs, border contracts and hashes remain
unchanged. The only new expectations are the specified negative-number colors:

- Reader golden follow-up identifies exact cells, original input/model hashes, old/new
  colors, and only changed PNG/SVG hashes for three existing reports. Complete model
  equality is still required after applying those explicit expected color patches.
- The 44-PDF appearance contract changes only main/Core `japanese-grid-report`: exactly
  one `-24` text span changes from black to red. An independent audit checks that every
  other glyph field, text, page geometry, paint, image and link remains identical.
- Historical writer comparisons bind the existing border contract and retained before
  PDFs. Full/Core invoice changes exactly three spans; full/Core grid changes one span.
  Exact new content/pixel hashes are separate from the old contracts; no thresholds
  were increased. Source snapshot changes were independently expanded and verified to
  affect only those cell colors. New finalized-command hashes retain all other frozen
  page-plan/diagnostic requirements.
- The appearance mutation test explicitly removes a number-format color and must fail.
  Existing missing-text, missing-border, shifted-content and pagination mutations remain.

## Additional forms and verification

The five original cases were rerun before/after Mapping with unchanged print settings:
invoice row repetition, manual page breaks, wrapped text, crossing images, pivot plus
opaque printer settings. There are 26 generated PDFs and four intentional main/Core
missing-pivot rejections. [Execution](additional-forms-execution.json),
[full extracted text/position/paint/image report](additional-forms-report.json), and
[contact sheets](additional-forms/) preserve the remaining differences. In particular,
manual/crossing-image pagination and Core's documented unsplit-image scope remain different
from LibreOffice. Available fixtures are repository examples and synthetic inputs, not
new user-supplied real reports. Desktop Microsoft Excel was not run.

Final local and CI results are recorded in `verification.json`. Earlier failed comparisons
caused by the now-correct red text were investigated, not accepted through looser gates.

Reproduce the targeted reference experiment after building Release:

```sh
python3 scripts/validate-pr113-display.py \
  --carlito /path/to/Carlito-Regular.ttf \
  --soffice /path/to/soffice \
  --run artifacts/pr113-display-new-run
```

The script requires a fresh output directory. Dynamic-library environment variables may
be needed for a locally extracted LibreOffice installation. It records font identity,
PDF evidence, rejection reasons, source hashes and remaining differences separately.
