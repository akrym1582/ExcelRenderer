# Published-package API sample / 公開パッケージのAPIサンプル

This sample uses **NuGet.org packages only**, not repository ProjectReferences.
It evaluates `A1*125` over an in-memory workbook and reads a generated XLSX file;
both checks require the result 375. No input file, rendering or fonts are needed.
Requires the .NET 10 SDK for this executable (the libraries target .NET Standard 2.1).

From the repository root, create an isolated console project outside the repository:

```bash
dotnet new console --framework net10.0 --output /tmp/excelrenderer-api-sample
cp samples/namespace-migration/Program.cs /tmp/excelrenderer-api-sample/Program.cs
dotnet add /tmp/excelrenderer-api-sample package ExcelRenderer.Mapping --version 1.9.1
dotnet run --project /tmp/excelrenderer-api-sample
```

Alternatively, replace the `ExcelRenderer.Mapping` package in the command with
`ExcelRenderer`, or reference both at 1.9.1. Each choice supplies the same public
Formulas/OpenXml assemblies. Do not add unpublished packages named
`ExcelRenderer.Formulas` or `ExcelRenderer.OpenXml`. Mapping includes XLSX/JSON
dependencies; the formula engine assembly itself uses only the BCL.

Expected output:

```text
ExcelRenderer.Formulas.Evaluation.FormulaEngine
engine: 375
ExcelRenderer.OpenXml.Reading.OpenXmlWorkbookReader
reader: 375
```

Pass `engine` or `reader` to run one path. The identity probes are separate methods
so the compatibility reproduction can resolve each old CLR type independently.

通常の利用例は上記のとおりです。リポジトリ外のコンソールプロジェクトで、NuGet.org の
Mapping または本体の1.9.1だけを参照します。インメモリ数式の計算と、生成した XLSX の
読み込みで375を確認します。描画・フォント・外部入力ファイルは不要です。

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
