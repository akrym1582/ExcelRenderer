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
| Pivot sources/caches | Flat explicit sources/output locations move independently of cache records; indexed shared IDs and pivot item positions/settings survive data changes, with new members appended and refresh requested | Parent cases, PivotMappingReviewTests, unchanged-part hash tests |
| Unsupported structures | Unknown/unsafe parts, grouped/calculated/external or unresolved named pivot sources, chart caches, threaded comments, macros/signatures/Strict rejected | Capability preflight and output protection tests |
| Sparkline extension | Same whole-book formula-preflight rejection as parent | Retained advanced-baseline.json and regression test |
| Dependencies | No ClosedXML or removed-only dependencies in active locks, assets, assemblies or packages | Dependency/package inventory and external package consumer |

Parent-generated fixtures retain the previous behavior as evidence. Intentional
structural corrections are explicit: internal hyperlink targets now follow the
referenced row; print areas include all final occurrences; pivot sources and
records are updated instead of retaining stale addresses/placeholders. Conditional
formatting and validation preserve a common sqref where relative meanings agree.
Per-cell rules can split with rebased formulas; population-dependent rules retain
one complete application set. Tests compare covered cells and resolved references. Empty comment parts may be retained with
empty comment/shape lists. Media bytes remain unchanged.

No formula or pivot aggregate evaluator was added. Grouped/calculated/external
pivot sources and unresolved named cache sources are outside the safe writer
contract. Source-record refresh does not calculate saved pivot aggregate cells.
Existing Reader display limitations are unchanged; use explicit custom number
formats for locale-specific date output.

The [validation record](closedxml-removal-validation.md) identifies the current
checks separately from historical Phase A/B evidence.

## Structural Mapping reference corrections (2026-10-16)

Pivot cache updates distinguish unchanged, source-only, records-only and item
changes. Source-only movement leaves records, shared items and all pivot settings
untouched; only worksheet source and output location addresses move. Pivot filter
AutoFilter rectangles are internal filter coordinates and remain unchanged.

For fields with pivot items, or used as a data field's base field, existing shared
IDs are retained by type and value. Numeric `1` and text `"1"` remain distinct;
member captions and other saved metadata do not participate in identity. New IDs
and pivot items append after all existing entries, including subtotal entries.
Missing members remain in these dictionaries with their original settings, so
row/column item positions, page selections, filter/pivot-area references and base
items keep their meaning across every table sharing the cache. Existing missing
item retention/refresh settings remain unchanged. Unindexed measure-only
shared dictionaries may be rebuilt because no pivot item/base-item references
observe their IDs. Changed records request refresh; saved aggregate cells are
not recalculated.

CF/DV formulas use the original sqref base, translate relative source rows/columns,
then follow structural row placement and the matching repeat/parent occurrence.
Absolute rows follow structural movement but do not acquire copy offsets. String
literals, function names and quoted sheet names survive. Equivalent applications
share one sqref; independently rebased per-cell rules retain priority, stopIfTrue
and validation messages. Different rules in one CF block can use different range
partitions. Color scales, data bars, icon sets, ranking, average and duplicate/unique
rules keep their entire surviving population. A population rule whose formulas
cannot be represented equivalently in one set fails before output publication.
Empty application sets are removed.

Standard formula elements and `cfvo type="formula"` val attributes use common
store detection for preflight and rewriting. Non-formula thresholds are unchanged.
External/structured/3D/array/spill syntax retains the existing conservative
restriction, including early rejection before data access for repeated templates.
Unknown x14 worksheet extensions remain unsupported. No expression evaluator was
added. Formula copy translations have a per-write 1,024-entry cache; occurrence
lookups use per-write indexes and do not retain caller data after writing.

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
