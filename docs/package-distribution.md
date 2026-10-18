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

## Consuming the public Formulas/OpenXml APIs

For **1.9.1**, install `ExcelRenderer.Mapping` for calculation and XLSX reading/writing
without rendering, or use the same APIs from `ExcelRenderer` if rendering is already
needed. For example:

```bash
dotnet add package ExcelRenderer.Mapping --version 1.9.1
```

No Formulas/OpenXml PackageReference, manual DLL reference or font package is
required. The Mapping package also installs its XLSX/JSON dependencies; only the
Formulas assembly itself is BCL-only. `ExcelRenderer.Tool` is a CLI installation,
not the supported way to reference these library APIs. See the
[package-only sample](../samples/namespace-migration/README.md) and
[1.9.0 → 1.9.1 migration](namespace-migration.md).

This is the **1.9.1/current** distribution contract. The released 1.9.0 main and
Mapping nupkgs instead depend on unpublished `ExcelRenderer.OpenXml >= 1.8.0`
and omit both shared DLLs. Clean NuGet.org-only restores fail with NU1101.
The 1.9.0 CLI does contain both DLLs and is the baseline for the
[released-API comparison](namespace-migration-validation.md). Do not apply the
current package inventory rules to that historical layout.

1.9.1 の API 利用には本体または Mapping のどちらかを追加します。Formulas／OpenXml の
個別パッケージや手動 DLL 参照は不要です。1.9.0 は配布構成が異なり、未公開 OpenXml 依存で
NuGet.org のみからの復元が失敗します。名前空間変更に対しては別途、using の更新と再ビルドが必要です。

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
Nine independent consumers exercise the calculation engine, OpenXml adapter, Mapping,
main plus Core, main plus Mapping, the namespace migration sample with main/Mapping/both,
and the installed CLI using only the generated packages.
`validate-openxml-packages.py` delegates to this current validation.

PR CI and the release workflow both enforce this contract. Release verification
runs before uploading packages or creating the release commit/tag. Historical
validation reports describe their recorded package layout; this contract replaces
their earlier standalone OpenXml/Formulas package assumptions.

NuGet 公開対象は本体・Mapping・Tool・Fonts の4つだけです。Core・OpenXml・Formulas は
非公開のソースプロジェクトとし、必要な DLL/XML を公開パッケージに同梱します。
テスト・サンプル・検証・性能測定の各 csproj も公開しません。新規プロジェクトも既定で
非公開にし、CI とリリース前の検証で公開対象の追加や未公開パッケージへの依存を検出します。

## Skia PDF native subset assets

Before a source build or pack, run `python3 scripts/build-harfbuzz-subset.py` on
an OS with CMake and a C++ compiler. It builds the adapter from checksum-pinned
HarfBuzz 10.2.0 without third-party runtime dependencies beyond the platform C/C++
runtime. Generated assets live under `native/runtimes/<rid>/native`; generated
`build.json` records source/binary hashes, version, license and verified exports.
`build/HarfBuzzSubset.targets` copies the current host asset for ProjectReference
consumers and includes native runtime assets in the main nupkg. The tool receives
its current host asset through the main ProjectReference. The HarfBuzz license is
included in both packages. Python/CMake are not installed for product consumers.

The release workflow builds and runs subset/API/CLI tests, packs the four packages
and runs isolated consumers independently on Linux
x64 and Windows x64 before collecting both assets for packing. A local one-OS pack
is only evidence for that OS; it does not establish a complete release inventory
or other-RID runtime support. See `docs/skia-pdf-validation.md` for actual local
verification and outstanding distribution gates. Missing native assets fail only
when Skia PDF is selected; PDFsharp and the Core standalone API retain their
existing native dependency contract.
