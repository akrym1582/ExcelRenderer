# ページ選択時の保存ピボット値検査

対象: PR #113 の `99028a55f1421082eb2b6e5b41b24b9dd3eca353`。

## 再現と修正

`PivotPageSelectionTests` のSDK生成Excelは、印刷範囲 `A1:F20`、10行目の後の手動改ページ、2ページ目の `F15` にあるピボット `Summary` を持つ。
保存値 `30` がある対照ブックで、対象シートが2ページになることを確認する。
数式 `1+1` と条件付き書式の式ルールも含む。

修正前のコードに回帰テストを追加して実行すると、CachedValue／Recalculate × 条件付き書式の有効／無効の4通りすべてで、1ページ目だけの変換が `PivotSavedValuesMissing` で失敗した。
CachedValueで条件付き書式が無効な場合は本読み込み、有効な場合およびRecalculateではページ事前計画の読み込みで例外になった。

共通Readerの欠落検出を維持し、通常版のページ指定変換だけはエラー候補を `CellReadDiagnostics` に保持する。
事前計画も同じ設定を引き継ぐ。既存のページ事前検査が、選択ページのソース領域との交差を判定してエラーを収集し、出力シンクを開く前に変換を停止する。
CoreとReader単体、ページ指定のない全ページ変換は即時拒否を維持する。
このため、印刷範囲がなく、保存セルの使用範囲より遠くにある欠落ピボットも、全ページ変換では引き続き拒否される。

## 回帰テストの確認対象

| 条件 | 期待結果 |
| --- | --- |
| 1ページ目のみ、上記4通り | 成功。ピボット診断なし |
| 2ページ目のみ、上記4通り | 欠落診断にシート、ピボット名、範囲、ページ番号を含めて拒否 |
| 全ページ、上記4通り | 欠落を明示して拒否 |
| PDF／PNG／SVG／PostScriptの全ページ・2ページ目、Markdownの全体出力 | 診断抑制・保持上限の設定があっても出力先を開かず拒否 |
| 印刷範囲外、明示した選択範囲外、非選択シートのピボット | 対象出力は成功 |
| 印刷範囲外だが繰り返し行／列タイトルとして選択ページに表示されるピボット | 拒否。共通Readerの印刷領域判定も修正 |
| 明示範囲指定で繰り返しタイトル自体を除外 | 成功 |
| Core、Reader単体 | 欠落ピボットを引き続き拒否 |
| Coreの非選択シート、印刷範囲外 | 対象出力は成功 |
| 保存値のあるピボット | 描画と更新要求の警告を維持 |

ピボット集計の再計算は追加していない。Recalculateも保存済みのピボット表示値を必要とする。

## 検証記録

ローカルの検証結果とGitHub CIの確認対象は別に記録する。
PR元コミットの [Unit tests #182](https://github.com/akrym1582/ExcelRenderer/actions/runs/42923480685) は8ジョブすべて成功。
Linux／WindowsのCore、通常版とCoreの比較、PDF配布検証を含む。
修正分のローカル結果を以下に記録する。更新後のリモートCI結果はPR #113のChecksを参照。

ローカル結果（Linux / .NET SDK 10.0.401）:

- Debug全体再ビルド、最終Release全体再ビルド: 警告0、エラー0。
- `source scripts/test-fonts.sh` 後の `dotnet test ExcelRenderer.slnx --no-restore -m:1`: 3,032件成功、失敗・スキップなし（通常版1,177、Core434、CLI135、Formulas833、Mapping226、OpenXml227）。
- その後追加したCoreの2ケースを含むReleaseゲート: 通常テスト428件、性能テスト8件成功。カバレッジは行83.11%、分岐72.10%（基準82.12%／70.08%）。
- 最終の1シート・2ページ構成の再現用Excelで、Releaseの `PivotPageSelectionTests` 17件成功。
- `scripts/validate-reader-isolation.py`: ReaderとCoreの独立restore/build/publish、依存グラフ、AssemblyRef、別プロセス実行が成功。
- `scripts/check-core-dependencies.py`、`scripts/check-source-namespaces.py`、`scripts/check-no-pdfsharp.py`、`git diff --check`: 成功。

テストから生成した欠落ピボットのExcel、保存値ありの対照Excel、1ページ目のみのPDF、検証ログとSHA-256一覧を、ローカルの `artifacts/pivot-page-selection/` に保存した。
再現用ExcelはテストのSDK生成入力であり、Excelアプリケーションでの操作による再現は実施していない。
