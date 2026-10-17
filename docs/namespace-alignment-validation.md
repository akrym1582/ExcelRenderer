# Formula documentation and namespace validation — 2026-10-17

最新mainの `dbb187c` から `docs/align-formula-reference-and-source-namespaces` を作成しました。
検証対象の実装は `2f70f8d929676dfc59146f863cf9a48b2dd1c4ac`（コミット前の同一実装の作業ツリー）。
環境はLinux / .NET SDK 10.0.401です。

[日英の関数リファレンス](formulas-reference.md)を40関数それぞれの実例・結果と、条件集計・
完全一致検索・日付系・文字列・TEXT書式の制約で整備しました。Formulas/OpenXmlの公開APIを含む
namespaceはフォルダに一致させています。[移行表](namespace-migration.md)に従って利用側の
using・完全修飾名を更新し、再ビルドしてください。コンパイラがBCLの名前を必要とする
IsExternalInitの2ファイルだけは、理由を明示したチェック対象の例外です。

| 検証 | 結果 |
| --- | --- |
| `check-source-namespaces.py` | 601宣言のフォルダ対応と、2件のコンパイラ用shimを確認 |
| 関数一覧と実装の照合 | 日英とも重複なし・実装と同じ40関数 |
| ドキュメントの式評価 | 日英それぞれ40関数と6書式、計92例が記載どおり |
| 全21プロジェクトのlocked restore | 成功 |
| Release Rebuild | 警告0・エラー0 |
| 全テスト | 1,939件成功、失敗・スキップ0 |
| solution pack | 本体・Mapping・Tool・Fontsの4パッケージのみ |
| パッケージ検査 | 未公開パッケージへの依存なし、同梱DLL/XMLのハッシュ一致 |
| 独立したNuGet利用 | engine、adapter、Mapping、本体+Core、本体+Mapping、インストール済みCLIの6通り成功 |
| Coreと本体の画像比較 | 17ページ成功 |
| 請求書サンプル3明細 | 合計66、XLSX/PDF/PNG/SVG/Markdown出力成功 |
| Oracle仕様比較 | 1,494比較一致、差分0。Excel実測0/498は未実施のまま |
| Oracle Pythonテスト | 5件成功 |
| Mapping性能ツールの生成コード | dict / 3行 / 2列のビルド・実行・出力検証成功 |
| 更新したドキュメントの相対リンク | 存在確認成功 |

テスト内訳はCore 82、Formulas 818（掲載例92件を含む）、Mapping 197、OpenXml 188、本体563、
Tool 91です。既存C#の171ファイルは、namespace・using・修飾名の変更を正規化すると処理本体が
一致しています。数式評価や資源上限のアルゴリズムを変更する作業ではなく、過去の性能測定値を
今回の変更で再測定した値として報告していません。既存の疎範囲・検索索引・資源上限の回帰テストは
上記の全体テストに含まれます。

主要コマンド（記録は [validation/namespace-alignment](validation/namespace-alignment)）：

```bash
python3 scripts/check-source-namespaces.py
python3 scripts/check-formulas-dependencies.py
python3 scripts/check-core-dependencies.py
dotnet restore ExcelRenderer.slnx --locked-mode
dotnet build ExcelRenderer.slnx -t:Rebuild -c Release --no-restore --verbosity:minimal -m:1
source scripts/test-fonts.sh
dotnet test ExcelRenderer.slnx -c Release --no-build -m:1 --logger trx --results-directory TestResults/NamespaceAlignment
dotnet pack ExcelRenderer.slnx -c Release --no-build --no-restore -p:BuildProjectReferences=false -o artifacts/namespace-alignment-packages
python3 scripts/package_distribution.py --packages artifacts/namespace-alignment-packages
python3 scripts/validate-formulas-packages.py --packages artifacts/namespace-alignment-packages --work /workspace/.namespace-package-consumers
python3 scripts/validate-core.py --visual-only
dotnet run --project samples/ExcelRenderer.Formulas.Sample -c Release --no-build -- /tmp/excel-namespace-invoice-3 3
```
