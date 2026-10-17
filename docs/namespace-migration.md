# Namespace migration

Formulas and OpenXml now use namespaces matching their physical source folders.
This is a **source and binary breaking API change**: update imports and qualified
type names, then rebuild dependent applications. Assemblies and package IDs retain
their names; NuGet publication remains limited to the main, Mapping, Tool and Fonts
packages. Existing fully qualified reflection/serialization type names also need
the new namespace.

公開APIも含めてフォルダとnamespaceを一致させました。以下の表に従ってusing・完全修飾型名を
更新し、利用側を再ビルドしてください。リフレクションなどに保存した型名も更新が必要です。
パッケージの公開対象は本体・Mapping・Tool・Fontsの4つです。

The type names in each row are unchanged. Their old namespace was the project root
shown in the table heading. Types already under `ExcelRenderer.OpenXml.Input`
retain that namespace.

## From `ExcelRenderer.Formulas`

| New namespace | Public types |
| --- | --- |
| `ExcelRenderer.Formulas.Data` | `FormulaRangeIndex`, `IFormulaDataSource`, `ISparseFormulaDataSource`, `InMemoryFormulaWorkbook` |
| `ExcelRenderer.Formulas.Diagnostics` | `FormulaDiagnostic` |
| `ExcelRenderer.Formulas.Evaluation` | `FormulaDateSystem`, `FormulaEngine`, `FormulaEvaluationOptions`, `FormulaEvaluationResult`, `FormulaSession`, `FormulaSessionStatistics` |
| `ExcelRenderer.Formulas.References` | `FormulaCellAddress`, `FormulaRange`, `FormulaRewriter` |
| `ExcelRenderer.Formulas.Syntax` | `FormulaNode`, `FormulaNodeKind`, `FormulaParseResult`, `FormulaParser`, `ParsedFormula`, `SourceSpan` |
| `ExcelRenderer.Formulas.Values` | `FormulaCellData`, `FormulaValue`, `FormulaValueKind` |

## From `ExcelRenderer.OpenXml`

| New namespace | Public types |
| --- | --- |
| `ExcelRenderer.OpenXml.Calculation` | `FormulaCalculationException`, `FormulaCalculationMode`, `FormulaTextResolver`, `WorkbookCalculationSession`, `WorkbookRecalculator` |
| `ExcelRenderer.OpenXml.Diagnostics` | `ReaderDiagnostic`, `ReaderDiagnosticCodes` |
| `ExcelRenderer.OpenXml.Formatting` | `CellDisplayResult` |
| `ExcelRenderer.OpenXml.Model.Cells` | `CellAddress`, `CellRange`, `CellRecord`, `CellValue`, `CellValueType`, `FormulaInfo`, `RichTextRunInfo` |
| `ExcelRenderer.OpenXml.Model.Drawing` | `ImageInfo` |
| `ExcelRenderer.OpenXml.Model.Printing` | `PrintRangeInfo`, `PrintReferenceInfo` |
| `ExcelRenderer.OpenXml.Model.Styles` | `CellStyleInfo`, `FontInfo` |
| `ExcelRenderer.OpenXml.Model.Workbook` | `ColumnInfo`, `DefinedNameInfo`, `ElementInfo`, `RowInfo`, `SheetInfo`, `WorkbookInfo`, `WorksheetInfo` |
| `ExcelRenderer.OpenXml.Reading` | `IWorkbookDataReader`, `OpenXmlReaderOptions`, `OpenXmlWorkbookReader` |
| `ExcelRenderer.OpenXml.Writing` | `OpenXmlWorkbookWriter`, `WorkbookWriteCapabilities` |
| `ExcelRenderer.OpenXml.Writing.Plans` | `CellValueEdit`, `OutputRowOccurrence`, `SheetEditPlan`, `WorkbookEditPlan` |

## Calculation engine example

```csharp
using ExcelRenderer.Formulas.Data;
using ExcelRenderer.Formulas.Evaluation;
using ExcelRenderer.Formulas.References;
using ExcelRenderer.Formulas.Syntax;

var source = new InMemoryFormulaWorkbook.Builder().AddSheet("Invoice").Build();
var formula = FormulaParser.Parse("TEXT(EOMONTH(DATE(2026,1,31),1),\"yyyy-mm-dd\")");
var result = new FormulaEngine().Evaluate(
    formula.Formula!, source, new FormulaCellAddress("Invoice", 1, 1));
// result.Value.Text == "2026-02-28"
```

`FormulaValue` and `FormulaValueKind` use `ExcelRenderer.Formulas.Values`.
`FormulaEvaluationOptions`, `FormulaDateSystem` and `FormulaSessionStatistics`
use `ExcelRenderer.Formulas.Evaluation`.

## Workbook and Mapping example

```csharp
using ExcelRenderer.Mapping;
using ExcelRenderer.OpenXml.Calculation;
using ExcelRenderer.OpenXml.Reading;

ExcelTemplateMapper.Map(template, output, data, new MappingOptions
{
    FormulaCalculationMode = FormulaCalculationMode.Recalculate,
});

// For an existing workbook:
WorkbookRecalculator.Recalculate(input, recalculatedOutput);
```

Reader configuration types are under `ExcelRenderer.OpenXml.Reading`; cell models
under `ExcelRenderer.OpenXml.Model.Cells`; edit plans under
`ExcelRenderer.OpenXml.Writing.Plans`. Fully qualified references such as
`ExcelRenderer.OpenXml.Calculation.FormulaCalculationMode.Recalculate` are useful
when a caller has a property with the same name. Main/Core rendering models,
Mapping types and CLI command namespaces keep their existing names.

## Source layout rules

All `FormulaSession` partial declarations live together under `Evaluation`.
Standalone function helpers use `Functions`, `Functions.Numeric`, `Functions.Dates`
and `Functions.Lookup`. `Functions.Dates` avoids shadowing `System.DateTime`.
The shared Core validation helper now uses `ExcelRenderer.Core.Validation`.

`python3 scripts/check-source-namespaces.py` enforces project root plus folder path
for declared namespaces in source, tests, samples and development tools. Top-level
entry points and assembly attributes do not declare a namespace. Two
`Compatibility/IsExternalInit.cs` compiler shims keep the required BCL name
`System.Runtime.CompilerServices.IsExternalInit`; renaming it would prevent the
compiler from recognizing `init` support. They are explicit, checked exceptions.
Shared files compiled through links use their physical source's namespace.

See [source organization](source-organization.md) for ownership and
[the function reference](formulas-reference.md) ([日本語](formulas-reference.ja.md))
for current calculation support. Historical validation reports retain their
recorded namespace/source paths and describe those commits.
