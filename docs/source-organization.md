# Source organization

Source namespaces match project root plus physical folders. Public namespace
changes are listed in [the migration guide](namespace-migration.md). Simple type names and
assembly names are unchanged. Historical
validation reports preserve their recorded paths and contracts.

| Project / folder | Namespace | Responsibility |
| --- | --- | --- |
| Formulas / Data | `ExcelRenderer.Formulas.Data` | Dense/sparse source contracts, immutable workbook and coordinate index |
| Formulas / Evaluation | `ExcelRenderer.Formulas.Evaluation` | Engine, all FormulaSession partials, options, date system and statistics |
| Formulas / Syntax | `ExcelRenderer.Formulas.Syntax` | Parser, AST, parse results and UTF-16 source spans |
| Formulas / References | `ExcelRenderer.Formulas.References` | Addresses, ranges and reference rewriting |
| Formulas / Values | `ExcelRenderer.Formulas.Values` | Typed cell data and formula values |
| Formulas / Diagnostics | `ExcelRenderer.Formulas.Diagnostics` | Calculation diagnostics |
| Formulas / Criteria | `ExcelRenderer.Formulas.Criteria` | Compiled criteria and bounded wildcard matching |
| Formulas / Functions | `ExcelRenderer.Formulas.Functions` | Single function registry and dispatch families |
| Formulas / Functions / Numeric | `ExcelRenderer.Formulas.Functions.Numeric` | Decimal rounding helper |
| Formulas / Functions / Dates | `ExcelRenderer.Formulas.Functions.Dates` | Excel calendar/serial helper |
| Formulas / Functions / Lookup | `ExcelRenderer.Formulas.Functions.Lookup` | Bounded session-local lookup index |
| OpenXml / Calculation | `ExcelRenderer.OpenXml.Calculation` | Workbook calculation, date adaptation, formula inspection and recalc/save APIs |
| OpenXml.Reader (root) | `ExcelRenderer.OpenXml.Reader` | Streaming reader and reader configuration |
| OpenXml / Writing | `ExcelRenderer.OpenXml.Writing` | Staged writer, capability checks and row prototype cache |
| OpenXml / Writing / Plans | `ExcelRenderer.OpenXml.Writing.Plans` | Typed cell, sheet, workbook and row occurrence edits |
| OpenXml.Reader / Model / Cells | `ExcelRenderer.OpenXml.Reader.Model.Cells` | Cell addresses, ranges, values, records, runs and formula metadata |
| OpenXml.Reader / Model / Styles | `ExcelRenderer.OpenXml.Reader.Model.Styles` | Font and cell style metadata |
| OpenXml.Reader / Model / Workbook | `ExcelRenderer.OpenXml.Reader.Model.Workbook` | Workbook, sheet, row, column, name and XML element metadata |
| OpenXml.Reader / Model / Drawing | `ExcelRenderer.OpenXml.Reader.Model.Drawing` | Neutral image metadata |
| OpenXml.Reader / Model / Printing | `ExcelRenderer.OpenXml.Reader.Model.Printing` | Print range/reference metadata |
| OpenXml.Reader / Formatting | `ExcelRenderer.OpenXml.Reader.Formatting` | Number formatting, display results and style reading |
| OpenXml / References | `ExcelRenderer.OpenXml.References` | Structural and formula reference transformations |
| OpenXml.Reader / Diagnostics | `ExcelRenderer.OpenXml.Reader.Diagnostics` | Reader diagnostics and operation metrics |
| OpenXml / Infrastructure | `ExcelRenderer.OpenXml.Infrastructure` | Writer-only relationship validation |
| OpenXml.Reader / Infrastructure | `ExcelRenderer.OpenXml.Reader.Infrastructure` | Shared XML values and borrowed stream helpers |
| OpenXml.Reader / References | `ExcelRenderer.OpenXml.Reader.References` | Print area/title parsing without formula ASTs |
| OpenXml.Reader / Input | `ExcelRenderer.OpenXml.Reader.Input` | Generic input validation and seekable memory/file spool |

Formula function families remain separate `FormulaSession.*.cs` files, with all
partial declarations in the same `Evaluation` namespace and folder. Private
session control, lazy branches, bounded cursors and retention limits stay shared.
Function helpers remain internal, and Formulas has only BCL runtime dependencies.

Core owns rendering geometry, shared traversal and PDF drawing. Main and Mapping
adapt their public models to Reader. Core references only Reader; OpenXml connects Reader and Formulas for editing/calculation and never references rendering, Mapping,
Core or font libraries. See [Core ownership](core-source-map.md).

Development-only Oracle and benchmark tools remain nonpackable. The shared Core
validation helper follows its physical `ExcelRenderer.Core.Validation` namespace
when compiled by both validation executables. NuGet publishes only the
[four distribution packages](package-distribution.md).

The namespace check covers source, tests, samples and tools. The only declared
namespace exceptions are the two compiler-required IsExternalInit shims documented
in the migration guide. Assemblies with only attributes and top-level programs
need no namespace declaration.
