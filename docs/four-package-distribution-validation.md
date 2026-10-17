# Four-package distribution validation — 2026-10-17

検証対象の実装は `08d6134567a481cb72a149f06fe332994ab310bd` です。
コミット前の同一実装の作業ツリーで以下を実行しました。Linux / .NET SDK 10.0.401。
数式拡張・性能測定の記録は [既存レポート](formulas-expansion-validation.md)、現在の配布契約は
[package-distribution.md](package-distribution.md) を参照してください。

| 検証 | 結果 |
| --- | --- |
| 全 csproj の MSBuild プロパティ評価 | 21件中、指定4件だけ `IsPackable=true`。残り17件は `false` |
| 公開対象 | ExcelRenderer / ExcelRenderer.Mapping / ExcelRenderer.Tool / ExcelRenderer.Fonts、すべて1.9.0 |
| locked restore | 成功 |
| Release Rebuild | 警告0・エラー0 |
| 全テスト | 1,847件成功、失敗・スキップ0 |
| solution pack | nupkgが指定の4ファイルだけ生成 |
| nuspec依存関係 | Core / OpenXml / Formulas の未公開パッケージへの依存なし |
| 同梱アセンブリ | 本体のCore、各パッケージのOpenXml/Formulas DLLが一致。ライブラリ同梱XMLも一致 |
| 独立した利用検証 | engine / adapter / Mapping / main+Core / main+Mapping / インストール済みCLI の6通りが成功 |
| 依存方向 | BCL限定のFormulasと、Core/OpenXmlの既存制約が成功 |

テスト件数は Core 82、Formulas 726、Mapping 197、OpenXml 188、本体563、Tool 91。
利用検証は新規の専用NuGetキャッシュとローカルソースマッピングを使い、公開対象4パッケージ
以外のExcelRendererパッケージやソースのProjectReferenceを利用していません。本体のみでのPDF・
Markdown出力と、本体・Mapping併用時の再計算・保存を確認しました。

実行コマンド（ログは [validation/four-package-distribution](validation/four-package-distribution)）：

```bash
dotnet restore ExcelRenderer.slnx
dotnet restore ExcelRenderer.slnx --locked-mode
dotnet build ExcelRenderer.slnx -t:Rebuild -c Release --no-restore --verbosity:minimal -m:1
source scripts/test-fonts.sh
dotnet test ExcelRenderer.slnx -c Release --no-build -m:1 --logger 'trx;LogFileName=four-package-distribution.trx' --results-directory TestResults/FourPackageDistribution
dotnet pack ExcelRenderer.slnx -c Release --no-build --no-restore -p:BuildProjectReferences=false -o artifacts/four-package-distribution
python3 scripts/package_distribution.py --packages artifacts/four-package-distribution --output /tmp/excel-four-packages-inventory.json
python3 scripts/validate-formulas-packages.py --packages artifacts/four-package-distribution --work /tmp/excel-four-package-consumers
python3 scripts/check-formulas-dependencies.py
python3 scripts/check-core-dependencies.py
```

この変更は配布・参照設定と検証・ドキュメントの変更です。数式評価のC#実装は変更しておらず、
性能測定値は既存レポートに記載した対象実装の結果です。Excel実機比較は引き続き未実施で、
以前のプロファイル比較結果をExcel実測として扱っていません。

`tests.log` は6テスト群すべての結果を保持します。同一のTRXファイル名を指定したため、
作業ディレクトリのTRX単体は最後のToolテスト群の結果だけを含みます。

## CI lockfile follow-up

[Core Linux CI](https://github.com/akrym1582/ExcelRenderer/actions/runs/38990564084/job/117271821069)
の画像比較開始時に、`ExcelRenderer.Core.Reference` のlocked restoreが`NU1004`で失敗しました。
配布設定変更後の参照グラフがそのlockfileに反映されていませんでした。通常のソリューションに
検証用2プロジェクトが含まれていなかったため、ソリューションのrestoreで検出できなかったものです。

`Core.Reference/packages.lock.json` を更新し、Core.ReferenceとCore.Validationを
`ExcelRenderer.slnx`に追加しました。全21件が通常のrestore/build対象となり、lockfileの
整合性検証を無効にせずに、以後の依存関係変更を早い段階で検出できます。

修正後、全21件のlocked restore、警告・エラー0件のRelease Rebuild、CIと同じ
`python3 scripts/validate-core.py --visual-only`（17ページ）を確認しました。

全1,847テストとsolution packも再実行して成功しました。生成nupkgは指定の4ファイルだけで、
未公開パッケージへの依存なし・同梱DLL/XMLのハッシュ一致も再確認しています。
