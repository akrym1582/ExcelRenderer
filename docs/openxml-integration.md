# OpenXml workbook reading and mapping

NuGet distribution contains only the main, Mapping, Tool and Fonts packages.
Formulas and OpenXml assemblies are bundled in the main and Mapping packages;
there are no separate packages for them. See [package distribution](package-distribution.md).
Install `ExcelRenderer.Mapping` 1.9.1 (or use `ExcelRenderer` 1.9.1) and import
`ExcelRenderer.OpenXml.Reading` for `OpenXmlWorkbookReader`. See the
[namespace migration](namespace-migration.md) and [package-only sample](../samples/namespace-migration/README.md).

> Explicit recalculation is available. The [formula guide](formulas.md) defines its scope, cache invalidation and supported features. The [support reference](formulas-reference.md) lists syntax and all 40 functions. Saved-value/formula-rejection behavior below describes the default CachedValue mode unless stated otherwise.

English | [日本語](https://github.com/akrym1582/ExcelRenderer/blob/main/docs/openxml-integration.ja.md)

Core, the main library and Mapping always use OpenXml. There is no engine
selection API or CLI switch. For existing callers, see the
[engine-selection migration](engine-selection-removal.md).
`ExcelRenderer.OpenXml` supplies the shared reader, writer and input handling. It
has no rendering, Mapping-language, font or ClosedXML dependency.

```csharp
using ExcelRenderer.Mapping;
using ExcelRenderer.Mapping.Configuration;

var document = new ExcelRenderer.Excel.ExcelReader().Read(input,
    new ExcelRenderer.Excel.ExcelReadOptions
    {
        FormulaDisplayMode = ExcelRenderer.Excel.FormulaDisplayMode.CachedValue,
    });

ExcelTemplateMapper.Map(template, output, data, new MappingOptions
{
    Input = new() { AllowTemporaryFiles = true },
});
```

Core exposes the equivalent `ExcelRenderer.Core.ExcelReadOptions` through
`PdfExportOptions.ReadOptions`. Main conversion and export options continue to
carry `ExcelReadOptions` through their existing `ReadOptions` properties.

| Formula policy | Behavior |
| --- | --- |
| CachedValue (default) | Streaming reader uses saved results; diagnostics are scoped to the selected output |
| Recalculate | Limited built-in calculator; see the formula guide |
| Legacy | Rejected before input or output |

In CachedValue mode the Reader uses only saved formula results. Stale results remain unchanged;
there is no freshness or dependency check. Missing results display empty text with
`FormulaCachedValueMissing`; saved zero, false and empty strings remain distinct.
Saved errors display their error text with `FormulaResultError`. Literal HYPERLINK
arguments may supply a static target, but never replace a saved or missing display.

Formula text is separate metadata. `ReportCell.Formula` contains only the original
nonempty body with a leading equals sign. Shared/array followers without a body
have null text and retain `FormulaSharedIndex` / `FormulaMasterAddress` on the cell
and Markdown `VisualCell`. Drawing does not expand formulas or fall back because
formula text cannot be expanded. The standalone low-level `FormulaTextResolver`
remains available to existing callers; it is not used by rendering.

The reader builds one OpenXml model. Source diagnostics continue to use the
selected sheets, rectangles, hidden dimensions and output pages. Input corruption,
limits, cancellation and execution errors propagate without retry.

Display culture is captured from CurrentCulture when opening the source. It is
passed explicitly to the low-level reader, which clones it for the read session.

Regional or unresolved displays are not silently treated as equivalent:
`UnsupportedNumberFormat`, `RegionalNumberFormat`, `IsoDateDisplay`,
`UnresolvedColor` and `UnsupportedPrintReference` are
reported by the reader and participate in the existing strict and
warnings-as-errors policy. In particular, built-in date format 14 retains the
OOXML `mm-dd-yy` interpretation, and ISO date cells retain source text. Use an
explicit custom number format when an exact localized date presentation is
required. Formula-cache errors and missing caches are diagnostics rather than
reasons to retry another reader.

The supported paper IDs retain the established point dimensions, including B4/B5;
landscape swaps width and height. Other paper IDs use A4 with UnsupportedPaperSize.
Explicit paperWidth/paperHeight lengths were not interpreted by the old Reader and
remain unused, with UnsupportedPaperDimensions; paperSize controls dimensions.
The values are never treated as raw PDF points.

`ExcelStyleConverter.Convert(IXLCell)` has been removed. Its replacement is
`Convert(CellStyleInfo style, CellValueType valueType)`. This is a breaking API
change: see the [migration example and value-type rules](closedxml-removal-compatibility.md#public-style-api-migration).

## Formula-free Mapping

OpenXml Mapping shares the existing expression parser, CLR/JSON evaluation and
once-evaluated expansion plan. It supports scalar
values, escape and format/date expressions, null, single-row arrays, nested
blocks, empty arrays, style-only cells, row/column metadata, compatible merges,
print areas/titles and manual/directive page breaks. New strings use inline
strings; unchanged rich/shared strings remain intact. Formula-looking mapped
strings remain strings. Date/time values preserve existing number formats and
receive default date/time formats only when the source format is General.

Before CLR getters, enumeration or expansion planning, Mapping scans the whole workbook for cell formulas (including empty
shared followers, arrays and data tables), table calculated/totals formulas and non-none totals functions, and
calculated defined names. Hidden/unselected sheets and rows removed by empty
arrays are included. Failure is `MappingException` with reason code
`MappingFormulaNotSupported` and source location when available. Unknown workbook,
worksheet or table extensions are conservatively rejected because absence of
value formulas cannot be established.

Calculate amounts, tax and totals in the caller and supply them through `**` value
placeholders. Static names accept only string/numeric/Boolean
constants and finite A1 reference unions (including whole rows/columns and quoted
known sheet names). Functions, arithmetic, dynamic/external/3D references, name
aliases and unrecognized grammar are rejected. Names overlapping repeat regions
remain unsupported. Static print areas/titles remain supported. Conditional
formatting and validation formulas are structural metadata. Their A1 references
move with rows and duplicated rules while string literals and function names stay
unchanged; no formula evaluation or new conditional rendering is performed.

After this preflight, Mapping validates structures before
getters or enumeration. Names, tables and picture anchors intersecting repeated
regions remain unsupported. References on other sheets are included. Names,
ordinary hyperlinks (including internal locations), comment cells and VML anchors,
pictures outside repeats, formula-free tables/filters, conditional formatting and
validation use final row occurrences. Media bytes and relationship targets are
preserved. Static worksheet pivot sources and output locations move; changed
source records and shared items are rebuilt and marked for refresh. Pivot aggregate
cells are not recalculated. Grouped/calculated/external or unresolved named pivot
sources, chart caches, threaded comments, macros, signatures, Strict packages and
unknown extensions/parts are rejected. Sparkline worksheet extensions retain the
existing formula-preflight rejection. There is no fallback or retry.

Mapping preserves the original `calcPr` (including its absence) without adding
formula recalculation requests. Print areas include all surviving output
occurrences and fully deleted areas are removed. Title rows intersecting repeated
or deleted regions are unsupported. Fixed or uniquely shifted titles are retained;
column titles remain unchanged.

A surviving `**@page-break` enables explicit scaling even at A1. Positive source
scale is retained, otherwise 100 is used. Empty arrays that remove a directive do
not change scaling. Existing row breaks follow every output occurrence; column
breaks remain in place.

Mapping `Input` shares Core's limits: 16 MiB in memory, 128 MiB input, 10,000 ZIP
entries and 512 MiB expanded ZIP data. Temporary files are disabled by default.
Enable them for large row stores or output stages. Callers own their streams.
Input and output streams must differ. The file overload rejects identical paths,
finishes a file in the destination directory, then replaces the destination.
All evaluation, capability and staged read-back checks finish before writing the
caller stream. A final-copy I/O failure can partially write that stream; no
rollback guarantee is made. Temporary storage is disposed on failures and
cancellation.

## CLI

```console
excelrenderer pdf input.xlsx -o result.pdf --formula-mode cached
excelrenderer xlsx template.xlsx --data data.json -o result.xlsx
excelrenderer render template.xlsx --data data.json --format svg -o output --formula-mode cached
```

All commands use OpenXml automatically. For mapping, supply `--data` (required
for `xlsx`). `--formula-mode` supports `cached` (default) and `recalculate`. The latter opts
into the limited built-in engine. Explicit `legacy` is rejected
before input processing or output creation.

## Ownership and verification

Generic input validation and the seekable memory/file spool live in
`ExcelRenderer.OpenXml/Input`; Core preserves its existing input types through
thin adapters. Core owns style conversion, merged-cell assembly, layout and
rendering. Full product hooks add runs, drawings, images and links to the neutral
model. Mapping owns the template language and final row occurrences; the writer
only receives typed edits. XML-present source cells are converted directly to
rendering cells rather than retaining a second complete cell-record collection.

The writer replays original rows from a spillable row store, writes a separate ZIP
stage, maintains package relationships and shared-string counts, then reads the
stage through the streaming reader. Parsed source rows are reused through a
per-sheet LRU cache: at most 64 prototypes, 256 KiB of original UTF-8 row XML in
total, and 64 KiB per row. These are source-size limits, not a claim of exact heap
size. Single-use and large rows bypass the cache; each output row edits a deep copy so values and
coordinates cannot leak between occurrences. Schema validation and unmodified-part hashes
are tested separately. See the
[validation record](https://github.com/akrym1582/ExcelRenderer/blob/main/docs/openxml-integration-validation.md)
for test commands, measured allocation costs and results at each recorded commit.

Literal `_xHHHH_` sequences and XML 1.0 control characters use SpreadsheetML escapes
and round-trip through the new reader. ClosedXML 0.105.1 does not decode these escapes
in plain inline strings; reopening these special values with that library exposes the
escaped spelling. Ordinary strings and leading-apostrophe quotePrefix behavior preserve their
established value semantics.
