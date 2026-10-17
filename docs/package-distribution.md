# NuGet package distribution

Only these four projects publish NuGet packages:

| Package | Contents |
| --- | --- |
| `ExcelRenderer` | Main library, Core, OpenXml and Formulas DLL/XML files |
| `ExcelRenderer.Mapping` | Mapping, OpenXml and Formulas DLL/XML files |
| `ExcelRenderer.Tool` | Installable CLI with one copy of each required assembly, including Fonts |
| `ExcelRenderer.Fonts` | Optional embedded fonts and their license notices |

`ExcelRenderer.Core`, `ExcelRenderer.OpenXml` and `ExcelRenderer.Formulas` retain
their assemblies, namespaces and source-project boundaries. They do not publish
separate packages. Tests, samples, benchmarks and validation tools are also
nonpackable. `Directory.Build.props` defaults new projects to nonpackable; only
the four distribution projects explicitly opt in.

The main library and Mapping work independently. Both include the same OpenXml
and Formulas assemblies because neither package requires the other. Their copies
must have identical SHA-256 hashes. The combined consumer check verifies that
an application using both packages compiles and runs. Keep their package versions
aligned when publishing. Main and Mapping export the shared reader's third-party
dependencies (`DocumentFormat.OpenXml` and `ExcelNumberFormat`) directly, without
dependencies on unpublished ExcelRenderer packages.

`build/EmbeddedOpenXml.targets` owns shared assembly inclusion. Internal project
references use `PrivateAssets="all"`, so source projects that directly use OpenXml
or Formulas APIs must declare their own project reference. Package consumers get
these APIs from the DLLs in the published main or Mapping package.

From the repository root, after a Release build:

```bash
python3 scripts/package_distribution.py
dotnet pack ExcelRenderer.slnx -c Release --no-build --no-restore -p:BuildProjectReferences=false -o artifacts/packages
python3 scripts/package_distribution.py --packages artifacts/packages --output artifacts/package-inventory.json
python3 scripts/validate-formulas-packages.py --packages artifacts/packages --work /tmp/excelrenderer-package-consumers
```

Use an empty package output directory. Checks evaluate `IsPackable`, `PackageId`
and `Version` with MSBuild for every csproj, require exactly four nupkg files,
reject unpublished package dependencies, and verify shared DLL/XML hashes.
Independent consumers exercise the calculation engine, OpenXml adapter, Mapping,
main plus Core, main plus Mapping, and the installed CLI using only the generated
packages. `validate-openxml-packages.py` delegates to this current validation.

PR CI and the release workflow both enforce this contract. Release verification
runs before uploading packages or creating the release commit/tag. Historical
validation reports describe their recorded package layout; this contract replaces
their earlier standalone OpenXml/Formulas package assumptions.

NuGet 公開対象は本体・Mapping・Tool・Fonts の4つだけです。Core・OpenXml・Formulas は
非公開のソースプロジェクトとし、必要な DLL/XML を公開パッケージに同梱します。
テスト・サンプル・検証・性能測定の各 csproj も公開しません。新規プロジェクトも既定で
非公開にし、CI とリリース前の検証で公開対象の追加や未公開パッケージへの依存を検出します。
