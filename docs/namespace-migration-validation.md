# 1.9.0 → 1.9.1 published API validation

Validated on **2026-10-17**, Debian 13, .NET SDK 10.0.401 / runtime 10.0.12,
against NuGet.org artifacts and repository base `fa09924` (1.9.1).
This report separates published-artifact evidence from checks on locally built packages.

## Artifacts and baseline

The reproduction downloads `ExcelRenderer`, `ExcelRenderer.Mapping` and
`ExcelRenderer.Tool` at exact versions 1.9.0 and 1.9.1 from the NuGet.org flat
container. [The retained inventory](validation/namespace-migration/released-api.json)
records all six nupkg SHA-256 hashes, relevant DLL/XML hashes, nuspec dependencies,
assembly versions, all namespace moves and command outcomes. Full logs and the
complete public type inventory are generated in the chosen work directory.

The **published** 1.9.0 main and Mapping nupkgs contain neither Formulas nor
OpenXml DLLs. Both depend on `ExcelRenderer.OpenXml >= 1.8.0`. On the validation
date the NuGet.org package indexes for `excelrenderer.openxml` and
`excelrenderer.formulas` returned HTTP 404. A new project and isolated NuGet cache
with NuGet.org as the sole source failed to restore either library package:

```text
error NU1101: Unable to find package ExcelRenderer.OpenXml.
No packages exist with this id in source(s): nuget.org
```

Consequently, a normal 1.9.0 library-package consumer cannot serve as a successful
baseline. The reproduction instead compiles real consumer code against the
Formulas/OpenXml DLLs **shipped in the published 1.9.0 CLI** (plus explicit
`DocumentFormat.OpenXml` 3.3.0 and `ExcelNumberFormat` 1.1.0 dependencies).
Both the engine and XLSX reader calls succeed with these old DLLs. This proves
the old type identities are present in released artifacts; it does not claim that
CLI DLL references are a supported library installation method or that 1.9.0
library-package restore succeeded. No product source is rebuilt for this baseline.

1.9.1 main and Mapping bundle both DLL/XML pairs, with identical hashes in both
packages. The CLI's copies of the DLLs also match. Their nuspecs have no dependency
on unpublished ExcelRenderer packages. Fresh 1.9.1 consumers restore successfully.

## Public type comparison

`System.Reflection.Metadata` reads exported public type definitions (including
public nested types) from all seven ExcelRenderer DLLs in each CLI payload,
without executing them. Comparing full CLR names gives:

| Assembly | 1.9.0 public types | 1.9.1 public types | Namespace moves | Newly added |
| --- | ---: | ---: | ---: | ---: |
| Formulas | 20 | 24 | 20 | 4 |
| OpenXml | 37 | 37 | 36 | 0 |
| Main / Mapping / Core / Fonts / Tool | 133 / 5 / 9 / 1 / 6 | 133 / 5 / 9 / 1 / 6 | 0 | 0 |

Formulas' 20 moved types include `InMemoryFormulaWorkbook+Builder` (CLR notation).
OpenXml's `Input.WorkbookInputOptions` retains its name. The four new types are
`Data.FormulaRangeIndex`, `Data.ISparseFormulaDataSource`,
`Evaluation.FormulaDateSystem` and `Evaluation.FormulaSessionStatistics`.
They were absent from the released 1.9.0 DLL and are not namespace migrations.

The script compares every moved type with the [migration table](namespace-migration.md),
and verifies that both README installation tables list exactly the four published
packages. The two changed assemblies retain **AssemblyVersion 1.8.0.0** and have
no forwarded types or definitions under the removed root namespaces. Unchanged
public type names in other assemblies are not evidence of unchanged signatures
or behavior; public signatures using moved types are affected too, such as
`MappingOptions.FormulaCalculationMode` and `ExcelStyleConverter.Convert`'s
`CellStyleInfo` / `CellValueType` parameters. This inventory is a type-name comparison,
not a full member-level or behavioral compatibility certification.

## Real consumer checks

The [committed sample](../samples/namespace-migration/Program.cs) evaluates an
in-memory formula to 375 and creates/reads a one-cell XLSX containing 375. The
legacy variant changes only imports to the former root namespaces. All projects
are generated outside the repository and use no ProjectReferences.

| Consumer scenario | Main only | Mapping only | Main + Mapping |
| --- | --- | --- | --- |
| Restore exact 1.9.0 packages from NuGet.org | NU1101 | NU1101 | Not needed; each fails independently |
| Compile old imports against 1.9.1 | CS0246 / CS0103 | CS0246 / CS0103 | CS0246 / CS0103 |
| Build/run updated imports against 1.9.1 | Both checks pass | Both checks pass | Both checks pass |
| Run old executable with only Formulas/OpenXml DLLs replaced by 1.9.1 copies | Both type probes fail | Both type probes fail | Both type probes fail |

The binary-swap check keeps the 1.9.0-compiled consumer DLL, deps.json and runtime
configuration unchanged. The consumer SHA-256 is recorded and asserted identical
in all three deployments. Only the two product DLLs are replaced; there is **no
consumer rebuild**. Separate methods resolve each type so the first failure does
not mask the second:

```text
System.TypeLoadException: Could not load type 'ExcelRenderer.Formulas.FormulaEngine'
from assembly 'ExcelRenderer.Formulas, Version=1.8.0.0, Culture=neutral, PublicKeyToken=null'.

System.TypeLoadException: Could not load type 'ExcelRenderer.OpenXml.OpenXmlWorkbookReader'
from assembly 'ExcelRenderer.OpenXml, Version=1.8.0.0, Culture=neutral, PublicKeyToken=null'.
```

## Reproduction and repository checks

```bash
python3 scripts/verify-namespace-migration.py --work /tmp/excelrenderer-namespace-check
```

Requires Python 3, .NET 10 and access to NuGet.org; use a new/empty work directory.
The script's nonzero-build/runtime checks require the expected compiler error or
exception and type names, rather than accepting arbitrary failures. Its successful
run includes **28 checks**, including artifact inspection, both legacy restore
failures, the old-DLL baseline, three source/deployment combinations and documentation
table checks. `report.json` and per-command logs are written to that directory.

Local source verification additionally used locked restore, a Release solution
rebuild with zero warnings/errors, **all 1,972 solution tests passing (no skips)**,
the namespace/dependency checks, four-package packing/inventory validation, and
**all nine isolated package consumers passing**, including the installed CLI.
The consumer validator now executes the committed sample against
main-only, Mapping-only and combined generated packages as part of existing CI.
Published NuGet tests and generated-package tests are separate evidence.

## Compatibility and versioning assessment / 互換性・バージョニング評価

**High compatibility risk for users of the moved APIs.** The 1.9.0 → 1.9.1 patch
increment contains a source and binary breaking public API change. Semantic
Versioning calls for a major version for this change unless the old API is
preserved. Automatic patch upgrades may fail at build time; precompiled libraries,
plugins and stored reflection/serialization identities may fail only at runtime.
The unchanged shared AssemblyVersion does not preserve missing type definitions.
The 1.9.0 restore defect can prevent reaching the namespace error at all and must
be diagnosed independently.

This PR aligns documentation and samples with the API that 1.9.1 actually shipped;
it does not restore old binary identities or change published package versions.
Future release choices are to restore and test full old-API compatibility through
facades, or publish the breaking API as a major version with explicit release notes.
Type forwarding alone cannot rename a type's namespace. Updating only assembly
version numbers or documentation also cannot make old binaries compatible.
Existing NuGet 1.9.1 artifacts, including their embedded README, must not be
overwritten; corrected package documentation requires a future release.

公開DLLで移動を確認した型は56個です。旧usingは1.9.1でコンパイルに失敗し、移行後は本体のみ・
Mappingのみ・両方の構成で数式評価とXLSX読み込みが成功しました。旧実行ファイルのDLLだけを
差し替えると、両代表型で TypeLoadException を再現しました。通常の1.9.0パッケージ復元は
未公開依存で失敗するため、旧APIのベースラインには公開済み1.9.0 CLIのDLLを使っています。
パッチ更新としての互換性リスクは高く、文書修正だけでは解消できません。今後のリリースでは
旧APIの互換性維持、または破壊的変更に対するメジャーバージョン更新が必要です。
