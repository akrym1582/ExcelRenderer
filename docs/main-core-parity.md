# Main and Core: choice and parity

[日本語](main-core-parity.ja.md) · [Diagnostics and delivery](diagnostics.md) · [Core contract](core.md)

Use main for multiple fonts, partial rich text, conditional formatting, shapes, links,
recalculation, range/page selection, trim, or PNG/SVG/Markdown/PostScript. Use Core for
PDF from saved values with one supplied static regular TrueType face. Core is bundled
in the main NuGet distribution; there is no standalone Core NuGet package.

| Contract | Relationship | Conditions / intended difference |
| --- | --- | --- |
| Ordinary values, number formats, cells, borders, fills, print layout | Common | Explicitly select the same sheets in order |
| Text geometry and column widths | Match with settings | Same physical drawing face; preserve original Normal metadata for MDW |
| Formula display | Match with settings | Main `CachedValue`; Core always saved values. Missing cache is empty, not recalculated |
| Size, color, underline | Common | Cell formatting with regular text |
| Bold, italic, partial runs | Intended difference | Core uses regular face, flattens run text into the cell style |
| Conditional formatting, shapes, PDF links | Intended difference | Core omits them; display text remains |
| IVS / fallback | Intended difference | Core removes selectors and uses the supplied face without discovery/fallback |
| Basic images | Common | Complete image, start anchor in one page, no title repetition |
| Image crop/rotation/flips, boundary crossing, repeated titles | Intended difference | Core stretches complete source and emits only on the start page |
| Cell rotation | Intended difference | Core ignores rotation |
| Default sheets | Current APIs agree | Both include hidden worksheets in workbook order; explicitly select sheets for stable comparisons |

`MainCoreParityTests` in [the main test project](../tests/ExcelRenderer.Tests/MainCoreParityTests.cs)
executes the public APIs using separate streams over identical XLSX bytes. Core's independent
test project has no new main/shared dependency. Main disables OS/font-pack discovery, selects
face 0 with `PreferRequested`, and sets the fallback family to the supplied Noto Sans JP.
Embedded font family, glyph IDs, unhinted advances and actual outline paths are checked against
the supplied static font. Each execution records input and font SHA-256.

| Fixture/test ID | Input and direct comparison | Independent expectation |
| --- | --- | --- |
| values | Japanese/ASCII, integer, decimal, negative, date, typed string/bool/error formula caches and missing cache | 123, 12.50, -42, 2024-01-01, saved, TRUE, #DIV/0!; missing-cache diagnostic |
| text | 14pt blue underlined centered/middle wrapped text, right/top ShrinkToFit | Multiple baselines, reduced font size, painted underline |
| dimensions-match / dimensions-mismatch | Normal matches Noto / Normal remains Calibri; explicit 20-unit columns, 30pt rows, merge, hidden row/column | Hidden text absent; first origin within 36–40pt for 0.5 inch left margin |
| borders | Red thin left, double right, dashed top, thin bottom, yellow fill, partial merged edges | Yellow paint and 0.5pt thin stroke; shared fixed border audit covers conflicts independently |
| portrait / landscape | A4 and 0.5 inch margins | Width 595.276 / 841.890pt from mm × 72 / 25.4 |
| scale / fit | 75% / two 500pt rows fitted to one page | 8.25pt text at 75%; Fit produces one page with reduced text size |
| breaks / titles / areas | Break after row 1 / repeat row 1 / two single-row print areas | Exactly two pages; title on both pages |
| empty | Empty selected worksheet | Exactly one page |
| selection | second, data; excluded third sheet | Exactly two pages in requested order, no excluded text |
| images | 3×3 red PNG, one-cell anchor at B2 | Exactly one image; hand-calculated B2 bounds (156, 66, 158.25, 68.25)pt and red pixels |
| header-footer | STATIC / PAGE &P | STATIC and PAGE 1 on the corresponding page |
| defaults | Row/column default styles on cells without explicit style | ROW/COLUMN and yellow paint |
| border-conflict | Adjacent thin/double competing edge | Winning double stroke has 0.75pt width |
| image-overlap | Red/blue pictures at the same B2 anchor | Two pictures with matching placement, pixels and order |

PdfPig independently compares every page, ordered duplicate letters and their baselines,
size/color, ordered painted paths (including widths/colors/commands), and image geometry/data.
Position/size tolerance follows the existing 0.01pt geometry helper in
[ReaderAnchorRegressionTests](../tests/ExcelRenderer.Tests/ReaderAnchorRegressionTests.cs)
(the writer normalizes boxes to 0.001pt); pages and text have
no tolerance. Path commands use exact numeric representation; the existing separate border audit
retains its stricter 0.002pt limit. Nonpainting clip paths can differ
between product adapters; these are not counted as painted borders. The existing Linux
`libreoffice_regression.compare` additionally checks full geometry and exact 144/288dpi pixels,
including the effect of clipping and paint order. It applies no alignment or mask.

The comparator rejects removed text, shifted content, missing/added borders and an extra page.
[Expected difference tests](../tests/ExcelRenderer.Tests/MainCoreExpectedDifferenceTests.cs) limit
rich-text, rotation, IVS, link and conditional-format changes to the relevant attribute/region;
the Core output must retain its baseline and unrelated main cells must retain their positions.
Existing Core image/font and main formula tests retain broader scope contracts (see [Core](core.md)).
Matching both products does not establish full Excel compatibility. The 44 frozen reviewed
PDFs and LibreOffice reference evidence remain separate and unchanged.

Run from the repository root (Linux: source `scripts/test-fonts.sh` in the test shell):

```sh
dotnet test tests/ExcelRenderer.Tests -c Release --filter 'FullyQualifiedName~MainCoreParityTests|FullyQualifiedName~MainCoreExpectedDifferenceTests'
python3 scripts/check-main-core-parity.py
```

Normal solution tests include semantic comparisons. Windows CI runs these comparisons without
requiring cross-platform pixel identity. Linux additionally runs the existing raster comparator.
Both PDFs, inputs, per-case JSON and difference images are retained in CI `SampleOutputs/MainCoreParity`.
See [execution evidence](validation/main-core-parity/README.md).

File examples are executable: [main](../samples/ExcelRenderer.Sample/PdfFileExample.cs),
[Core](../samples/ExcelRenderer.Core.Sample/Program.cs). Both close a unique adjacent temporary
file before replacing the destination, remove it on failure, and support Ctrl+C. This preserves
the previous output if conversion fails; filesystem/power-loss durability is not promised.
See [sample commands](../samples/ExcelRenderer.Sample/README.md). Repository samples use
ProjectReference; installed consumers reference the published `ExcelRenderer` package.

Existing independent difference contracts are reused: [Core PdfTests](../tests/ExcelRenderer.Core.Tests/PdfTests.cs) (regular face normalization), [PageAndImageTests](../tests/ExcelRenderer.Core.Tests/PageAndImageTests.cs) (start-page-only pictures, ignored transforms and omitted shapes), [SavedFormulaTests](../tests/ExcelRenderer.Core.Tests/SavedFormulaTests.cs) (saved values only), and [main recalculation](../tests/ExcelRenderer.Tests/FormulaRecalculationTests.cs).
