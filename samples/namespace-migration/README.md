# Published-package API sample / 公開パッケージのAPIサンプル

This sample uses **generated NuGet packages only**, not repository ProjectReferences. It evaluates A1*125 over an in-memory workbook and reads a generated XLSX; both paths require 375. No rendering, fonts or external input are needed. The executable uses .NET 10 and the libraries target .NET Standard 2.1.

The current source uses the new OpenXml.Reader namespace. Run it against packages built from this revision using the isolated consumer validator (which tests main, Mapping and both):

```bash
python3 scripts/validate-formulas-packages.py --packages artifacts/packages-reader-split --work /tmp/excelrenderer-api-consumers
```

Build the four packages first as described in [distribution](../../docs/package-distribution.md). Do not add unpublished Reader, Formulas or OpenXml package IDs. Main and Mapping bundle the same shared assemblies. The source split does not assign a release version; released 1.9.1 uses the previous OpenXml.Reading namespace.

Expected output:

```text
ExcelRenderer.Formulas.Evaluation.FormulaEngine
engine: 375
ExcelRenderer.OpenXml.Reader.OpenXmlWorkbookReader
reader: 375
```

Pass `engine` or `reader` to run one path. The identity probes are separate methods
so the compatibility reproduction can resolve each old CLR type independently.

このサンプルは分離後のReader名前空間を使います。同じソースから生成した本体／Mapping／両方のパッケージだけで検証してください。公開済み1.9.1のAPIとは区別し、未公開Readerパッケージへの参照は追加しません。

For the **historical compatibility check**, run:

```bash
python3 scripts/verify-namespace-migration.py --work /tmp/excelrenderer-namespace-check
```

Use a new or empty work directory. Optionally add `--downloads /path/to/nupkg-cache`
to reuse downloads. This downloads the released main, Mapping and CLI packages
for 1.9.0 and 1.9.1, records hashes and exported public types, and checks:

- Clean 1.9.0 main/Mapping restores fail on the unpublished OpenXml dependency.
- This sample with old imports compiles and runs against DLLs shipped in the 1.9.0 CLI.
  This is a released-DLL baseline, **not** a successful 1.9.0 library-package restore.
- Old imports fail to compile with 1.9.1 main/Mapping/both; migrated imports pass.
- Keeping the old executable unchanged and replacing only the two DLLs produces
  `TypeLoadException` for each old type, for each 1.9.1 package choice.

`report.json` and per-command logs go into the work directory. The script never
builds the repository's product sources. Existing CI also compiles/runs this
sample against locally generated main/Mapping/both packages through
`scripts/validate-formulas-packages.py`.

旧APIの検証では1.9.0 CLIに同梱された公開済みDLLを使います。通常の1.9.0本体・Mappingの
復元は未公開依存により失敗するため、両者を区別します。1.9.1での旧usingのビルド失敗、
using更新後の成功、旧バイナリのDLL差し替えによる型解決失敗をそれぞれ検証します。
詳しくは[移行ガイド](../../docs/namespace-migration.md)と[検証記録](../../docs/namespace-migration-validation.md)を参照してください。
