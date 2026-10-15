# Standalone Open XML reader

`ExcelRenderer.OpenXml` is a nonpackable .NET Standard 2.1 library for reading rendering data from XLSX files. Its runtime dependencies are DocumentFormat.OpenXml **3.3.0** and ExcelNumberFormat **1.1.0**. It has no dependency on ClosedXML, Core, Mapping, fonts, or rendering libraries. Existing converters and CLI options continue to use their existing readers. This is an independent reader for comparison and future integration, not a completed ClosedXML replacement.

## Usage and input ownership

```csharp
using var input = File.OpenRead("validated.xlsx");
using var reader = OpenXmlWorkbookReader.Open(input, new OpenXmlReaderOptions
{
    SelectedSheetNames = new[] { "Report" },
    Culture = CultureInfo.GetCultureInfo("ja-JP"),
    DiagnosticSink = diagnostic => Console.Error.WriteLine(diagnostic.Code),
});

var sheet = reader.Sheets.Single(s => s.Name == "Report");
var info = reader.ReadWorksheetInfo(sheet.Index);
foreach (var cell in reader.EnumerateCells(sheet.Index))
{
    var style = reader.ResolveStyle(cell);
    var display = reader.FormatCell(cell);
    // Consume immediately; retaining every cell is the caller's decision.
}
```

Import `ExcelRenderer.OpenXml`, `System.Globalization`, and `System.Linq` for this example. This low-level API requires an already validated, readable, seekable input at position zero. A higher layer must perform upload validation, ZIP validation and spooling; these are not duplicated here. The caller's stream stays open after success, failure, enumeration interruption and reader disposal. Stream position is not restored.

Sheet indexes are **original one-based workbook positions**, not sheet IDs or relationship IDs. `Sheets` includes hidden/veryHidden sheets and chart sheets without renumbering. Accessing chart-sheet cells or worksheet metadata throws `NotSupportedException`. Selection uses ordinal, case-sensitive names: null selects all worksheets, empty selects no bodies, duplicate names are deduplicated, and unknown names fail at Open. Explicit `ReadWorksheetInfo` is allowed for an unselected worksheet; enumeration is not.

Only one worksheet enumeration or metadata operation can be active per reader. A second active operation throws `InvalidOperationException`, including a cached metadata request. Dispose enumerators, including on early termination. `foreach` handles this automatically. Use separate readers for parallel work; this instance is not a general thread-safe service. `ResolveStyle` and `FormatCell` are available during enumeration. All reader operations/properties throw `ObjectDisposedException` after disposal. `CellRecord` and its nested metadata remain usable as snapshots after enumeration and reader disposal; style resolution requires a live reader.

## Traversal and retained memory

Open reads workbook metadata, styles and theme, but no worksheet body or shared strings. A worksheet's first request performs a metadata pass, then enumeration reads the part again. Metadata holds row definitions, normalized column intervals, merges, formula masters and a row interval index for array formulas. It never holds a cell dictionary or a whole worksheet DOM. Enumeration loads one SDK cell at a time and projects it into independent immutable metadata. Breaking, errors and cancellation dispose the current part reader; re-enumeration starts from the beginning.

Shared strings load on the first `t=s` cell, entry by entry. **The complete shared-string table is retained**, even when only a small selected sheet uses it. There is no disk index or process-global intern/cache. A rich string's immutable runs are shared; null run properties mean “inherit this component from the cell font.” Resolve the cell's font independently before overlaying explicitly present run properties. Phonetics do not enter display text, but their presence and storage cost are recognized. OOXML `_xHHHH_` decoding is applied exactly once; `_x005F_x0041_` remains literal `_x0041_`.

Memory therefore grows with shared strings, style tables, row/column metadata, merges and formula groups. It is **not constant memory** and the limits below are not a process RSS cap. Two worksheet passes and per-cell DOM/projection cost can make this implementation slower than ClosedXML for dense sheets. See the [measured validation](openxml-reader-validation.md).

| Option | Default | Meaning |
| --- | ---: | --- |
| `MaxCharactersInPart` | 256 Mi | XML characters per part, enforced by SDK 3.3.0 |
| `MaxSharedStringItems` | 1,000,000 | Shared-string entries |
| `MaxSharedStringCharacters` | 32 Mi | Aggregate source UTF-16 text, including phonetics |
| `MaxStyleRecords` | 100,000 | Entries in each style table |
| `MaxCellTextCharacters` | 1 Mi | Source UTF-16 text in one cell or string entry, including formula/phonetic text |

Limits must be positive. Exceeding a limit throws `InvalidDataException`; there is no truncation or fallback reader. XML parser/part-limit failures are wrapped in `InvalidDataException` with their inner exception. Cancellation tokens are checked in traversal loops and at each entry, not during the SDK's synchronous load of one element. Culture is a read-only snapshot taken at Open. The per-reader number-format cache retains at most 8,192 codes.

## Data and source units

Every XML-present `c` is returned, including style-only cells, saved empty strings, and cells inside merges. No missing cells or merge anchors are synthesized. `RawDimension` is an untrusted hint; actual bounds come from coordinates. Missing row/cell coordinates are inferred in XML order. Conflicting, duplicate, reversed or out-of-grid coordinates fail. `CellAddress.Parse` accepts plain coordinates; `CellRange.Parse` additionally accepts dollar signs for absolute A1 references.

`CellValue.Kind` is `Blank`, `String`, `Number`, `Boolean`, `Error`, or `IsoDate`. Numbers retain the finite double and `CellRecord.RawValue` separately. Boolean values must be 0/1; false displays `FALSE`. ISO date text is validated and retained with its original timezone spelling. No UTC/local conversion occurs. Invalid numbers/shared indexes fail rather than becoming blanks.

| Metadata | Unit / representation |
| --- | --- |
| `RowInfo.HeightPt`, `DefaultRowHeightPt` | Points; absent default row height uses reader default 15 |
| `ColumnInfo.Width`, `DefaultColumnWidthExcel` | Original Excel width; no ClosedXML correction or font measurement |
| `BaseColumnWidthCharacters` | Character count, OOXML default 8 |
| `PageMargins` attributes | Inches |
| `PageSetup` / `PageSetupProperties` | Original IDs/attributes, scale separate from fitToPage; zero fit is retained |
| `HeaderFooter` | Original control-code text, without expansion |
| `RowBreaks` / `ColumnBreaks` | Individual id/min/max/man attributes; count attributes are not trusted |
| `ElementInfo` | Immutable name/attributes/children/leaf text, with no SDK objects |

Rows preserve custom height, hidden, customFormat/style, outline and collapsed. Columns remain intervals; later overlapping definitions replace earlier intervals. `SheetFormat` preserves absence versus explicit zero in the original attributes, including `zeroHeight`. Duplicate merges are removed; partial overlaps fail. Print unions retain their ordering and do not become bounding rectangles.

Print definitions retain raw text and original zero-based `localSheetId`. The parser supports same-sheet A1 rectangles, whole rows and whole columns, including quoted commas and doubled apostrophes in sheet names. External books, 3D references, functions/OFFSET and indirect names emit `UnsupportedPrintReference`; the **entire definition** then has no resolved ranges.

## Styles and display

Style precedence is explicit cell `s` (including zero), row `s` when `customFormat=true`, column style, then cell-XF zero. Saved components are used even with `apply*=false`; omitted components fall back through `xfId`, then defaults. Original apply flags and quotePrefix remain in `CellStyleInfo.Attributes`. Normal font selection uses builtinId zero and its style-XF, not a localized style name. Missing styles.xml permits an implicit General style; absent font information remains absent for a future adapter to resolve.

Font, fill, border (including diagonals), alignment, rotation 255, wrap, shrink and indentation metadata are retained. Theme major/minor Latin, East Asia and script font information is exposed through `Workbook.Theme`; it is not a font fallback engine. Colors preserve RGB/indexed/theme/auto attributes. Where possible a color adds `resolvedRgb`, using theme/indexed lookup and HLS luminance tint. Unresolved colors retain their source and emit a diagnostic rather than becoming black.

Numbers and text sections are formatted with ExcelNumberFormat 1.1.0. Custom codes take priority. Built-in IDs **0–22 and 37–49** have explicit mappings. Other regional built-ins (23–36, 50–81, etc.) without explicit codes, unverified locale directives (`[$…]`), DBNum and era formats fall back to General with `UnsupportedNumberFormat`. This deliberately conservative policy may exclude some formats the dependency could render. Invalid codes also fall back with a diagnostic. Width-dependent hashes, repeat/spacing layout and format colors are not connected to a renderer.

Numeric serials are passed unmodified with the library's `isDate1904` argument. Serial 60 is not forced into `DateTime`. The fixed library's floating-point elapsed-time output can be `27:29:60` for the nearest double to 27.5 hours; this is recorded and not corrected with text substitutions.

## Formula caches, diagnostics and feature boundaries

Only saved results are read. `FormulaInfo.HasCachedResult` distinguishes missing results from saved zero, false and empty text. A missing result displays empty text with `FormulaCachedValueMissing`; an Excel error displays its original text with `FormulaResultError`. Neither formulas nor external links are evaluated. Shared followers keep their own original text (often empty), si and master address/text/ref, even when the master appears later. Relative references are not rewritten. Array followers are identified only for XML-present cells through a row interval index. Dynamic/data-table metadata remains raw; advanced spill tracking is outside this reader.

Diagnostic codes are defined in `ReaderDiagnosticCodes`. Diagnostics have code, severity, optional original sheet/address and a description without cell contents or URIs. `DiagnosticSink` receives events without accumulating them inside the reader. `FormatCell` also returns its own small diagnostic list. Unresolved-style-color diagnostics are emitted when the style is first resolved; they are not replayed on every format call.

`HasDrawings`, `HasHyperlinks`, `HasConditionalFormatting` and `HasTables` report presence only. Reading their XML does not imply rendering support. Image binaries, DrawingML shapes, hyperlink interpretation, conditional-format evaluation, editing/insertion/saving, general formula evaluation and PDF/PNG/SVG/Markdown integration are outside this implementation. In particular, a HYPERLINK formula without a cache remains blank.

## Future ownership and integration

| Existing owner | Standalone counterpart | Later integration work |
| --- | --- | --- |
| Core `CoreExcelReader`, `FormulaCacheReader` | `OpenXmlWorkbookReader`, `CellRecord`, `FormulaInfo` | Adapt streaming values to rendering models, then delegate/remove duplicated XML reading |
| Core `WorkbookLayoutMetadataReader` | `WorksheetInfo`, print references | Adapt source dimensions and page metadata; remove old whole-sheet DOM path when migrated |
| Core `ReaderSession` | Style table and `FormatCell` | Map diagnostics and metadata to existing policies |
| Public/Core style converters | `CellStyleInfo`, rich runs | Build rendering-style adapter and apply run overrides with each cell's font |
| Core column-width/font measurement | No replacement | Measure original Excel widths and convert to points in Core |
| Full reader extension points | No replacement | Redesign ClosedXML-dependent hooks in a separate change |
| Core input preparer; Mapping | No replacement | Preserve upload validation/spooling and editing/saving responsibilities |

Existing public `ExcelStyleConverter.Convert(IXLCell)` is preserved. No existing product project references the new reader, and it is not included in existing NuGet packages. Validation commands and evidence are in [the validation tool README](../tools/ExcelRenderer.OpenXml.Validation/README.md).
