# ClosedXML removal compatibility ledger

Baseline: `feature/openxml-integration`, `24c9dea4d67190951d152eef56f76abc943ef733`.
Implementation: `feature/remove-closedxml` working tree. This includes breaking API
changes; no release, package publication or remote push is included.

| Contract | Implemented behavior | Evidence |
| --- | --- | --- |
| Reader / Mapping default | OpenXml | ReaderBackendContractTests, StructuralMappingTests |
| Auto | Same OpenXml path, no fallback/retry | Reader and Mapping integration tests |
| Explicit ClosedXml | Obsolete numeric value 0; fails before input/data/output | Public API and CLI contract tests |
| Unknown enum | ArgumentOutOfRangeException | Contract tests |
| Reader formulas | Original saved results only; no calculation | FormulaDisplayTests and formula-mode CLI tests |
| Mapping formulas | Whole-book preflight, including hidden/deleted rows, table formulas and computed names | FormulaPreflightTests |
| Number formatting | Existing ExcelNumberFormat path; regional/ISO/unsupported diagnostics remain visible | Reader tests and retained parent projections |
| Paper IDs and public dimensions | Established dimensions, tint/color and full-model behavior retained | ReaderBackendContractTests, parent render goldens |
| Style API | Convert(CellStyleInfo, CellValueType) replaces Convert(IXLCell) | ExcelStyleConverterTests; migration below |
| Scalar/nested/empty mapping | Existing parser, one data evaluation, typed edits and row metadata | MappingTests, MappingExpansionTests |
| Print areas/titles | All surviving occurrences; deleted areas removed; repeated title rows rejected | OpenXmlMappingTests, StructuralMappingTests |
| Names, hyperlinks, pictures, comments, tables/filter, CF/DV | Known references and anchors follow row placement across sheets | 40 parent-fixture cases plus cross-sheet, boundary and schema tests |
| Pivot sources/caches | Flat explicit worksheet sources and output locations move; changed source records/shared items are rebuilt and marked for refresh | Four parent pivot cases, unchanged-part hash test |
| Unsupported structures | Unknown/unsafe parts, grouped/calculated/external or unresolved named pivot sources, chart caches, threaded comments, macros/signatures/Strict rejected | Capability preflight and output protection tests |
| Sparkline extension | Same whole-book formula-preflight rejection as parent | Retained advanced-baseline.json and regression test |
| Dependencies | No ClosedXML or removed-only dependencies in active locks, assets, assemblies or packages | Dependency/package inventory and external package consumer |

Parent-generated fixtures retain the previous behavior as evidence. Intentional
structural corrections are explicit: internal hyperlink targets now follow the
referenced row; print areas include all final occurrences; pivot sources and
records are updated instead of retaining stale addresses/placeholders. Conditional
formatting and validation may use several equivalent ranges; tests compare their
covered cells and formula references. Empty comment parts may be retained with
empty comment/shape lists. Media bytes remain unchanged.

No formula or pivot aggregate evaluator was added. Grouped/calculated/external
pivot sources and unresolved named cache sources are outside the safe writer
contract. Source-record refresh does not calculate saved pivot aggregate cells.
Existing Reader display limitations are unchanged; use explicit custom number
formats for locale-specific date output.

The [validation record](closedxml-removal-validation.md) identifies the current
checks separately from historical Phase A/B evidence.

## Public style API migration

```csharp
using var reader = ExcelRenderer.OpenXml.OpenXmlWorkbookReader.Open(input);
var cell = reader.EnumerateCells(reader.SelectedWorksheetIndices[0]).First();
var style = ExcelRenderer.Excel.ExcelStyleConverter.Convert(
    reader.ResolveStyle(cell), cell.Value.Type);
```

Pass a resolved style from the same workbook. Null styles throw
ArgumentNullException. General alignment uses the stored value type (including
formula cache types): numbers/ISO dates align right, booleans center, and other
types left. Missing formula caches use Blank; no formula is evaluated. Unknown
value categories throw ArgumentOutOfRangeException. The public style conversion
preserves the source font and rotation; Core applies its own single-font policy
when building a Core document.
