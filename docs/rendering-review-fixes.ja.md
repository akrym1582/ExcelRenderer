# 描画高速化のレビュー指摘への対応

対象は1.10.5の`df74eb0`。直近の本体高速化前の`a71e21d`も、同じ環境・入力で別に比較した。
[詳細な手順・結果](rendering-review-fixes.md)と[全測定記録](validation/rendering-review-fixes/)を参照。

## 対応した3点

1. **CLOCKの余分な一周走査**：退避・置換で空いたスロット番号を保持し、その位置へ直接格納する。
   `Scans`は退避処理だけでなく空き位置探索も数えるようにした。
   容量8/512での連続追加の走査数、保持値、置換、保持量制約による複数退避を回帰テストで検証する。
2. **preflightの状態分散**：複数のboolを`PageBuildPurpose`の通常描画・Core文字収集・
   メタデータ付きpreflightへ統合した。planが目的を検証・確定し、配置からcommand生成まで同じ値を渡す。
   セル文字→Z順の図形文字→ヘッダー/フッターの収集順はCoreのgeneratorへ集約する。
   trim/range/診断/observerの適用条件、CF装飾・図形だけのページでの通常経路へのfallbackを維持する。
3. **文字キャッシュの役割**：既定の計測とrun補完は同じmeasurerを共有する。
   custom measurer指定時の独立した補完は維持する。
   通常の非折り返し文字はlayout cacheだけで保持し、line cacheへの二重登録を省く。
   折り返し・部分書式の行断片と、互換/部分書式の候補幅は再利用を維持する。

通常文字fixtureでは、layout cacheに2,000回のhitがある一方、line cacheは2,000回のmissと
hitなしだった。修正版では、このline cache処理が発生しない。
折り返しfixtureのline cacheには71,984回のhitがあり、修正前後で一致する。
キャッシュ全廃ではなく、役割の重複を除いた。保持上限・公開API・native資源の所有期間は拡大していない。

## 性能の確認

保存済みRelease runtimeを交互順序・3ラウンドで実行した。ビルド・テスト・他の描画とは重ねない。
同一の入力bytesを使い、DLL・入力・フォントhash、cold/warm、生データを保持した。
以下は`text1000-Pdf`の各ラウンドの時間変化。負数が短縮。

| 比較 | Round 0 | Round 1 | Round 2 |
| --- | ---: | ---: | ---: |
| CLOCK修正のみ対1.10.5（warm 3、sample 5） | -16.5% | -30.8% | -22.7% |
| キャッシュ役割整理の追加効果（warm 3、sample 5） | +3.4% | +12.1% | -22.2% |
| 最終版対1.10.5の主比較（warm 2、sample 3） | +34.7% | -16.6% | -6.8% |
| 最終版対1.10.5の追加比較（tiering無効、warm 5、sample 8） | -5.5% | -6.1% | -13.3% |
| 最終版対本体高速化前（warm 3、sample 5） | -6.3% | -14.0% | -22.5% |

通常文字の追加比較での割り当て量は約0.6%減。本体高速化前との比較では約5.5%減だった。
役割整理だけの時間差には一貫した改善がなく、独立した高速化率として主張しない。
不要な登録/保持量計上を除く効果と、割り当て量の削減として評価する。

全22ケースの主比較で時間/RSSが5%を超えた17ケースを追加比較した。
範囲trimの時間差は主比較+7.2/+39.4/+10.0%、追加比較+1.3/+1.7/+1.4%。
部分書式PDFの追加比較は+4.5/+2.3/-1.1%だった。
他のケース・RSSには5%超過が残るため、全件5%非退行とは扱わない。
悪化原因をノイズだけとも断定しない。ピークRSSはプロセス累積高水位で、試行単位の追加量ではない。
条件を変えた結果を主比較へ差し替えず、[全ラウンドの判定](validation/rendering-review-fixes/regression-analysis.json)を残す。

通常文字で見つけた余分な処理は除去し、追加比較・本体高速化前との比較では短縮を確認した。
これは当該入力・環境での結果であり、すべての帳票の改善を保証するものではない。

## 出力の確認

PDFは12ケース・108ページで、96DPIの画素、文字/glyph座標、図形、画像、リンク、
ページ寸法、埋め込みfont subset hashが一致した。PNG/SVGの12成果物も、SVGのresource IDを
正規化した上で一致した。9件のlayout比較は行幾何・run・glyph・UTF-8/clusterのhashが一致した。
観測した最終変換はactive page payload最大1、native資源の生成/解放数が一致した。
Windowsはローカル未実行。

## 回帰検証

Debug/Releaseのsolution Rebuildはいずれも警告0・エラー0。
Releaseの全3,087件（Core 470、本体1,196、Tool 135、Formulas 833、Mapping 226、OpenXml 227）が成功した。
Coreのcoverage対象462件はline 84.90%・branch 74.36%で基準82.12%/70.08%を通過し、
Performanceカテゴリ8件も成功した。

公開シグネチャ比較は本体1,894/Core 68項目で追加・削除なし。最終Rebuildの製品DLL hashは
測定した最終版と一致した。namespace・所有境界・禁止依存の検査、Reader/Coreの独立した
restore/build/publish・graph/AssemblyRef・別プロセス実行も成功した。
project/package構成・依存版は変更していない。[検証ログとhash一覧](validation/rendering-review-fixes/README.md)を保持する。
