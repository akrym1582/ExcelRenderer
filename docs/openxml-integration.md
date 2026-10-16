# Selecting the Open XML engines

The default reader and mapper remain ClosedXML. `ExcelRenderer.OpenXml` is now a
packable shared dependency of Core, the main library and Mapping. It contains no
rendering, Mapping-language, font, or ClosedXML dependency. NuGet publication is a
separate operation.

```csharp
var document = new ExcelRenderer.Excel.ExcelReader().Read(input,
    new ExcelRenderer.Excel.ExcelReadOptions
    {
        Backend = ExcelRenderer.Excel.ReaderBackend.OpenXml,
        FormulaDisplayMode = ExcelRenderer.Excel.FormulaDisplayMode.CachedValue,
        BackendSelected = selection => Observe(selection.SelectedBackend, selection.ReasonCodes),
    });

ExcelTemplateMapper.Map(template, output, data, new MappingOptions
{
    Backend = MappingBackend.Auto,
    Input = new() { AllowTemporaryFiles = true },
    BackendSelected = selection => Observe(selection.SelectedBackend, selection.ReasonCodes),
});
```

Core exposes the equivalent `ExcelRenderer.Core.ExcelReadOptions` through
`PdfExportOptions.ReadOptions`. Main conversion and export options continue to
carry `ExcelReadOptions` through their existing `ReadOptions` properties.

| Reader policy | Formula policy | Selection |
| --- | --- | --- |
| ClosedXml (default) | Legacy / CachedValue | Compatibility reader |
| OpenXml | CachedValue | Streaming reader, with deferred source diagnostics |
| OpenXml | Legacy / Recalculate | Fails before output |
| Auto | Legacy | ClosedXML, `LegacyFormulaMode` |
| Auto | CachedValue | Inspects source capabilities before choosing |
| Any | Recalculate | Remains unsupported |

OpenXml preserves saved formula results and never evaluates formulas. Literal
HYPERLINK labels are restored before missing-cache diagnostics. Shared-formula
followers translate relative, absolute and mixed A1 references, ranges, complete
row/column references, literals, sheet names and functions. Unsupported shared
formula grammar (including structured/external or 3D references) is diagnosed as
`FormulaTextUnavailable`; Auto selects the compatibility reader when it affects
the output. This tokenizer does not edit Mapping formulas.

Reader Auto uses the selected sheets, explicit rectangles and hidden dimensions
for display capability checks. Page selection uses the same layout and source
regions as normal diagnostic filtering. That case builds a candidate model and
performs a font-gated layout preflight; an unsupported candidate is released
before loading ClosedXML. This extra work is part of Auto's cost. Input corruption,
limits, cancellation and execution errors propagate without a retry in another
engine.

Regional or unresolved displays are not silently treated as equivalent:
`UnsupportedNumberFormat`, `RegionalNumberFormat`, `IsoDateDisplay`,
`UnresolvedColor`, `UnsupportedPrintReference` and `FormulaTextUnavailable` are
reported on the explicit path and participate in the existing strict and
warnings-as-errors policy. In particular, built-in date format 14 retains the
OOXML `mm-dd-yy` interpretation, and ISO date cells retain source text. Use an
explicit custom number format when an exact localized date presentation is
required. Formula-cache errors and missing caches are diagnostics rather than
reasons to retry another reader.

## Formula-free Mapping

OpenXml Mapping shares the existing expression parser, CLR/JSON evaluation and
once-evaluated expansion plan with the compatibility writer. It supports scalar
values, escape and format/date expressions, null, single-row arrays, nested
blocks, empty arrays, style-only cells, row/column metadata, compatible merges,
print areas/titles and manual/directive page breaks. New strings use inline
strings; unchanged rich/shared strings remain intact. Formula-looking mapped
strings remain strings. Date/time values preserve existing number formats and
receive default date/time formats only when the source format is General.

Capability inspection covers the whole workbook before input data is evaluated.
Explicit OpenXml reports every unsupported reason and fails before output. Auto
uses ClosedXML for formulas on any sheet, general defined names, conditional
formatting, validation, tables/pivots/filters/sparklines, drawings/comments/links,
external data, macros/signatures/Strict OOXML, and unknown worksheet elements,
extensions or relationship parts. Empty `tableParts` emitted by ClosedXML are
accepted; actual table references are rejected. Repeated-region formula/name/
merge/table/picture restrictions still apply on the compatibility path.

Mapping Auto also uses `PrintAreaExpansionCompatibility` when a print area
intersects an expandable region, until that geometry pattern is proven equivalent
to ClosedXML. OpenXml's explicit rule extends each area over all its output row
occurrences and removes fully deleted areas. Title rows intersecting repeated or
deleted regions are unsupported. Fixed title rows and uniquely shifted title
bands are retained. Column titles remain unchanged.

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
excelrenderer pdf input.xlsx -o result.pdf --reader-backend openxml --formula-mode cached
excelrenderer xlsx template.xlsx --data data.json -o result.xlsx --mapping-backend auto --verbose
excelrenderer render template.xlsx --data data.json --format svg -o output --mapping-backend openxml --reader-backend openxml --formula-mode cached
```

`--reader-backend closedxml|openxml|auto` is available on pdf, image, svg,
markdown/md and render. `--mapping-backend` is available on all of them and xlsx;
explicit use requires `--data`. `--verbose` writes engine decisions to stderr.
OpenXml plus Legacy is rejected rather than silently changing formula policy.

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
stage through the streaming reader. Schema validation and unmodified-part hashes
are tested separately. See the integration validation record for measured evidence;
historical reader-only reports do not establish rendering integration results.

Literal `_xHHHH_` sequences and XML 1.0 control characters use SpreadsheetML escapes
and round-trip through the new reader. ClosedXML 0.105.1 does not decode these escapes
in plain inline strings; reopening these special values with that library exposes the
escaped spelling. Ordinary strings and leading-apostrophe quotePrefix behavior match
the legacy mapper.
