# Open XML source reader

`ExcelRenderer.OpenXml` is a packable .NET Standard 2.1 shared reader/writer library. Runtime dependencies are DocumentFormat.OpenXml **3.3.0** and ExcelNumberFormat **1.1.0**; it has no Core, Mapping, ClosedXML, font or renderer dependency. Product integration, selection and the conservative writer are described in [engine selection](openxml-integration.md). ClosedXML remains the default engine.

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

Import `ExcelRenderer.OpenXml`, `System.Globalization`, and `System.Linq` for this example. This low-level API requires an already validated, readable, seekable input at position zero. Product adapters use the shared `Input` preparation/spool implementation before this low-level call. The caller's stream stays open after success, failure, enumeration interruption and reader disposal. Stream position is not restored.

Sheet indexes are **original one-based workbook positions**, not sheet IDs or relationship IDs. `Sheets` includes hidden/veryHidden sheets and chart sheets without renumbering. Accessing chart-sheet cells or worksheet metadata throws `NotSupportedException`. Selection uses ordinal, case-sensitive names: null selects all worksheets, empty selects no bodies, duplicate names are deduplicated, and unknown names fail at Open. Explicit `ReadWorksheetInfo` is allowed for an unselected worksheet; enumeration is not.

Only one worksheet enumeration or metadata operation can be active per reader. A second active operation throws `InvalidOperationException`, including a cached metadata request. Dispose enumerators, including on early termination. `foreach` handles this automatically. Use separate readers for parallel work; this instance is not a general thread-safe service. `ResolveStyle` and `FormatCell` are available during enumeration. All reader operations/properties throw `ObjectDisposedException` after disposal. `CellRecord` and its nested metadata remain usable as snapshots after enumeration and reader disposal; style resolution requires a live reader.

## Traversal and retained memory

Open reads workbook metadata, styles and theme, but no worksheet body or shared strings. A worksheet's first request performs a metadata pass, then enumeration reads the part again. Metadata holds row definitions, normalized column intervals, merges, formula masters and a row interval index for array formulas. It never holds a cell dictionary or a whole worksheet DOM. Enumeration reads SDK start/end events and leaf text directly without constructing cell or shared-string-item DOMs. Only formula elements, partial run properties and small metadata entries use DOM projection. Breaking, errors and cancellation dispose the current part reader; re-enumeration starts from the beginning.

Shared strings load on the first `t=s` cell, entry by entry. **The complete shared-string table is retained**, even when only a small selected sheet uses it. There is no disk index or process-global intern/cache. A rich string's immutable runs are shared; null run properties mean “inherit this component from the cell font.” Resolve the cell's font independently before overlaying explicitly present run properties. Phonetics do not enter display text, but their presence and storage cost are recognized. OOXML `_xHHHH_` decoding is applied exactly once; `_x005F_x0041_` remains literal `_x0041_`.

Memory therefore grows with shared strings, style tables, row/column metadata, merges and formula groups. It is **not constant memory** and the limits below are not a process RSS cap. Two worksheet passes and per-cell DOM/projection cost can make this implementation slower than ClosedXML for dense sheets. See the [initial validation](openxml-reader-validation.md) and the [subsequent optimization measurements](openxml-reader-optimization.md).

| Option | Default | Meaning |
| --- | ---: | --- |
| `MaxCharactersInPart` | 256 Mi | XML characters per part, enforced by SDK 3.3.0 |
| `MaxSharedStringItems` | 1,000,000 | Shared-string entries |
| `MaxSharedStringCharacters` | 32 Mi | Aggregate source UTF-16 text, including phonetics |
| `MaxStyleRecords` | 100,000 | Entries in each style table |
| `MaxCellTextCharacters` | 1 Mi | Source UTF-16 text in one cell or string entry, including formula/phonetic text |

Limits must be positive. Exceeding a limit throws `InvalidDataException`; there is no truncation or fallback reader. XML parser/part-limit failures are wrapped in `InvalidDataException` with their inner exception. Cancellation tokens are checked in traversal loops and at each entry, not during the SDK's synchronous reading of one leaf text or metadata element. Culture is a read-only snapshot taken at Open. The per-reader number-format cache retains at most 8,192 codes.

## Data and source units

Every XML-present `c` is returned, including style-only cells, saved empty strings, and cells inside merges. No missing cells or merge anchors are synthesized. `RawDimension` is an untrusted hint; actual bounds come from coordinates. Missing row/cell coordinates are inferred in XML order. Conflicting, duplicate, reversed or out-of-grid coordinates fail. `CellAddress.Parse` accepts plain coordinates; `CellRange.Parse` additionally accepts dollar signs for absolute A1 references.

`CellValue.Type` provides the `CellValueType` enum for adapter dispatch. The existing `Kind` string remains `Blank`, `String`, `Number`, `Boolean`, `Error`, or `IsoDate`. Numbers retain the finite double and `CellRecord.RawValue` separately. Boolean values must be 0/1; false displays `FALSE`. ISO date text is validated and retained with its original timezone spelling. No UTC/local conversion occurs. Invalid numbers/shared indexes fail rather than becoming blanks.

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

Only saved results are read. `FormulaInfo.HasCachedResult` distinguishes missing results from saved zero, false and empty text. A missing result displays empty text with `FormulaCachedValueMissing`; an Excel error displays its original text with `FormulaResultError`. Neither formulas nor external links are evaluated. Shared followers keep their own original text (often empty), si and master address/text/ref, even when the master appears later. Raw snapshots are not rewritten; `FormulaTextResolver.TryResolve` separately expands supported shared followers for product formula metadata. Array followers are identified only for XML-present cells through a row interval index. Dynamic/data-table metadata remains raw; advanced spill tracking is outside this reader.

Diagnostic codes are defined in `ReaderDiagnosticCodes`. Diagnostics have code, severity, optional original sheet/address and a description without cell contents or URIs. `DiagnosticSink` receives events without accumulating them inside the reader. `FormatCell` also returns its own small diagnostic list. Unresolved-style-color diagnostics are emitted when the style is first resolved; they are not replayed on every format call.

`HasDrawings`, `HasHyperlinks`, `HasConditionalFormatting` and `HasTables` report
presence; `ConditionalFormattingRanges` retains rule source ranges without rule
bodies. The reader does not evaluate conditional formatting. `ReadImages` returns
part/relationship identity, content type and anchor metadata without loading bytes;
`OpenImageStream` opens caller-disposed image data valid only while the reader is
alive. Transfer required data before disposing the reader. Shared part URIs identify
reused image bytes independently of names and placements. Shape/link interpretation
and rendering remain product responsibilities. Low-level `FormatCell` still leaves
uncached HYPERLINK text blank; the product adapter restores supported literals. The
separate writer accepts typed formula-free plans, never general formula evaluation.

## Product integration

Core owns source-to-rendering adaptation, model assembly, fonts and layout. Full
hooks add product runs, drawings and links. Generic input preparation/spooling is
shared in OpenXml/Input. Mapping owns syntax/evaluation and sends final occurrences
to the conservative writer. See [engine selection](openxml-integration.md) for
current APIs and scope. The compatibility reader keeps its ClosedXML-only helpers.
Public `ExcelStyleConverter.Convert(IXLCell)` remains available.

## Adapter boundary for Core and the full library

`OpenXmlWorkbookReader` implements `IWorkbookDataReader`. The interface exposes source snapshots and stored-value formatting; profiling counters stay on the concrete reader. `SelectedWorksheetIndices` supplies original workbook positions in workbook order and excludes chart sheets, while `Sheets` retains all sheets. The full adapter also receives the original RenderRequest selection to decide chart-only diagnostics; body indexes do not replace the product request. Core and the full product consume this boundary through streaming adapters. This is a consumer boundary for consuming adapters, not a general third-party backend construction API; snapshot constructors remain internal.

```csharp
IWorkbookDataReader source = reader;
foreach (var index in source.SelectedWorksheetIndices)
{
    var geometry = source.ReadWorksheetInfo(index);
    // ResolveStyle(int) also works for geometry.Rows/Columns styles and style zero.
    foreach (var cell in source.EnumerateCells(index))
    {
        var style = source.ResolveStyle(cell.StyleIndex);
        var display = source.FormatCell(cell);
        var type = cell.Value.Type;
        foreach (var run in cell.RichTextRuns)
        {
            var font = style.FontProperties.WithOverrides(run.FontProperties);
            // The product adapter applies its own fallback, color and drawing policy here.
        }
    }
}
```

`FontProperties` on a style, `DefaultFontProperties` on the workbook and `FontProperties` on each rich run are immutable `FontInfo` views. Name and size (pt), bold/italic/strike, underline, vertical alignment, theme scheme and color remain nullable when unspecified. The original `Font`/`Properties` metadata is also retained. `WithOverrides` changes only explicitly supplied fields: false and underline `none` override true/single; null inherits. It does not measure/substitute fonts or normalize text. The composed font survives reader disposal. Do not store a composed font in the shared-string table: the same run may be used with different cell fonts.

The current ownership, backend routing, diagnostic filtering and writer guarantees
are described in [the integration guide](openxml-integration.md). Snapshot
constructors remain internal; this is not a third-party backend-construction API.
