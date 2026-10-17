# Namespace migration

In **1.9.1**, Formulas and OpenXml use namespaces matching their physical source folders
([PR #83](https://github.com/akrym1582/ExcelRenderer/pull/83)).
This is a **source and binary breaking API change**: update imports and qualified
type names, then rebuild dependent applications. Assemblies and package IDs retain
their names; NuGet publication remains limited to the main, Mapping, Tool and Fonts
packages. Existing fully qualified reflection/serialization type names also need
the new namespace.

公開APIも含めてフォルダとnamespaceを一致させました。以下の表に従ってusing・完全修飾型名を
更新し、利用側を再ビルドしてください。リフレクションなどに保存した型名も更新が必要です。
パッケージの公開対象は本体・Mapping・Tool・Fontsの4つです。

The tables below list types present in the **released 1.9.0 CLI DLLs**, not just
the parent commit of PR #83. Their simple names are unchanged; their old namespace
was the project root shown in the table heading. There are **56 moved public types**:
20 Formulas types (including the nested `InMemoryFormulaWorkbook.Builder`) and
36 OpenXml types. `ExcelRenderer.OpenXml.Input.WorkbookInputOptions` retains its
namespace. Four newly added Formulas types are listed separately below.

公開済み 1.9.0 DLL と比較すると、移動した公開型は入れ子の Builder を含めて56個です。
新規追加の4型は移動対象と区別しています。**1.9.1 はパッチ番号の更新でも破壊的変更です。**
通常のパッチ更新として扱わず、以下の手順で移行してください。

## Upgrade steps / 更新手順

1. Install **either** `ExcelRenderer.Mapping` 1.9.1 (no rendering required) or
   `ExcelRenderer` 1.9.1. Both bundle `ExcelRenderer.Formulas.dll` and
   `ExcelRenderer.OpenXml.dll` and expose their public APIs. If using both
   packages, align their versions. Do not add PackageReferences for unpublished
   Formulas/OpenXml package IDs; remove old direct references or private-feed
   copies that would conflict with the bundled DLLs.
2. Update `using` directives, aliases and fully qualified names using the tables.
   Adding only `using ExcelRenderer.Formulas;` or `using ExcelRenderer.OpenXml;`
   does not import child namespaces. Update type names saved in reflection,
   dependency-injection configuration or serialization metadata as applicable.
3. Rebuild every dependent library/plugin and application, then test deployment.
   Replacing DLLs alone fails CLR type resolution. Old and new Formulas/OpenXml
   assemblies both identify themselves as **1.8.0.0**; this does not preserve the
   missing old type identities. Assembly binding redirects cannot rename types.

本体または Mapping の1.9.1を参照し、未公開の Formulas／OpenXml への直接参照を整理します。
using・完全修飾名・保存済み型名を更新し、依存するライブラリやプラグインも含めて再ビルドします。
ルート名前空間の using だけでは子名前空間は取り込めません。DLL の置き換えやバインドリダイレクト
だけでは移行できません。

```bash
dotnet add package ExcelRenderer.Mapping --version 1.9.1
```

**1.9.0 restore caveat:** the published main and Mapping packages depend on
`ExcelRenderer.OpenXml >= 1.8.0`, which is unavailable from NuGet.org. They do not
bundle Formulas/OpenXml DLLs. A clean NuGet.org-only restore fails with NU1101;
do not mistake this packaging defect for a compiler namespace failure. The old
API can be examined in the published 1.9.0 CLI payload or an existing deployment.
The [validation report](namespace-migration-validation.md) distinguishes that
released-DLL baseline from a normal package consumer.

1.9.0 の本体・Mapping は NuGet.org に存在しない OpenXml パッケージに依存し、クリーンな復元は
NU1101 で失敗します。旧APIの検証には公開済み1.9.0 CLI内のDLLを使い、通常のパッケージ復元の
成功とは区別しています。1.9.1 では両DLLを同梱するため、この未公開依存は解消されています。

## Versioning risk / バージョニング上のリスク

**High for callers of the moved APIs.** A 1.x patch update is expected to preserve
public compatibility under Semantic Versioning; these namespace renames require
a major version unless the old API identities and signatures are preserved.
There are no old-namespace compatibility types or type forwarders in the released
1.9.1 DLLs. Consumers using reflection, serialized type identities or precompiled
plugins can fail even when the application itself compiles. For example,
`MappingOptions.FormulaCalculationMode` still exists, but its public enum type
has moved to `ExcelRenderer.OpenXml.Calculation`.

This documentation correction cannot repair an already published binary. For a
future release, either preserve and verify the old API with compatibility facades,
or ship the breaking API under a major version with explicit release notes.
`TypeForwardedTo` alone cannot fix a renamed namespace: forwarding preserves a
type's full name. Do not overwrite 1.9.1 artifacts or treat a later version bump
as retroactively making 1.9.1 compatible. NuGet's embedded README also requires a
new package release to reflect documentation corrections.

影響するAPIを使う利用側のリスクは高く、SemVer上は互換性維持またはメジャーバージョン更新が
必要な変更です。このPRの文書修正だけで公開済み1.9.1のバイナリ互換性は回復しません。
今後は旧APIを保持した互換ファサードを検証するか、破壊的変更をメジャー版とリリースノートで
明示する必要があります。型転送だけでは名前空間の変更は吸収できません。

## From `ExcelRenderer.Formulas`

| New namespace | Public types |
| --- | --- |
| `ExcelRenderer.Formulas.Data` | `IFormulaDataSource`, `InMemoryFormulaWorkbook`, `InMemoryFormulaWorkbook.Builder` |
| `ExcelRenderer.Formulas.Diagnostics` | `FormulaDiagnostic` |
| `ExcelRenderer.Formulas.Evaluation` | `FormulaEngine`, `FormulaEvaluationOptions`, `FormulaEvaluationResult`, `FormulaSession` |
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

## New public types in 1.9.1 / 新規追加の公開型

These types were absent from the released 1.9.0 Formulas DLL. They do not require
an old-to-new namespace replacement:

| Namespace | Added public types |
| --- | --- |
| `ExcelRenderer.Formulas.Data` | `FormulaRangeIndex`, `ISparseFormulaDataSource` |
| `ExcelRenderer.Formulas.Evaluation` | `FormulaDateSystem`, `FormulaSessionStatistics` |

## Calculation engine example

The following APIs are available after installing either published library
package above. See the [complete runnable sample](../samples/namespace-migration/README.md)
for both engine and XLSX reader calls and the [distribution contract](package-distribution.md).

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
using ExcelRenderer.Mapping.Configuration;
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
the Mapping entry point and CLI command namespaces keep their existing names.

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
