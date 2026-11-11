# Core performance optimization

実施日: 2026-11-11。添付の実装計画の5項目を対象とする。
変更前基準は `8fc3ec11554f001f82fda63be25ebfe02edd2a9a`。
初期checkout `06280e538df4ebc49989efcb3f239379ffa1e09b` は移行前の実装だったため、
cleanな作業ブランチを最新mainへfast-forwardしてから着手した。
計画書の調査基準 `514ee6e0e979e8ae3bbb061cd2479e1cec7930d4` からのCore差分は
[基準差分](validation/core-performance-optimization/plan-baseline-delta.txt)に保存した。
Reader・列幅・pivot・paginationの後続修正を保持し、旧実装を上書きしていない。

## 実装

| Phase | 変更 | 互換性と所有者 |
| --- | --- | --- |
| 1 | `GenerateText`がセルlayer 3と共通header/footer生成を逐次使用。収集用ページでは空セル配置、結合罫線fragment、border-only収集、画像配置、罫線競合解決、背景/画像コマンド生成を省略 | Coreの収集周だけに適用。本体preflightのtrim/図形/リンク/診断は既存経路。ヘッダー時刻と文字配置は本描画と共有 |
| 2 | 段落glyphを一度取得し、文字要素の境界indexと整数hmtx advanceを累積。確定行へglyphを引き継ぐ | 正常UTF-16かつ非color outlineに限定。判定は従来と同じ`totalUnits * size / unitsPerEm`。行ごとのUTF-8/clusterを再生成。color、不正UTF-16、カスタム/本体measurerは互換経路 |
| 3 | 上流はindexのsortと隣接重複除去。`SelectedPageCells`が元順序・一意・selection済みの契約を受け渡す | plan経路ではページ内BandIndex、順序辞書、GroupBy/OrderByを省略。独立layout経路は汎用selectionを維持。候補はページごとに解放 |
| 4 | `(normalized text, size, effective wrap, necessary width)`をキーにし、512件CLOCKへ変更 | 色/下線等を除外。折り返しなし/非正幅は幅を共通化。縮小はcache外。入力2048文字以下、保持量131072単位まで |
| 5 | `WritePositions`がSkia builderのposition Spanへ直接書き込み。rendererごとにlazyなfill/stroke Paintと最後のdash effectを再利用 | 最大Paint 2個、effect 1個。double加算とfloat変換位置を維持。既存サイズ別SKFont cacheは維持。blob/builder cacheは追加しない |

保持量の単位は「キー文字数 + 確定行文字数 + glyph数」の合計。
byte単位の実メモリ見積りではなく、件数制限と組み合わせた保持量制限である。
UTF-8、cluster、advance配列はこの単位に比例して有界になる。
CLOCKはhit時に参照bitだけを更新し、必要なentryだけを退避する。

SkiaSharp 4.151.0の`SKPositionedTextRunBuffer.Positions`は書き込み可能なSpanを返す。
`AllocatePositionedTextRun`から`Build`までの同一同期呼出し内だけで借用し、
そのSpanを保存・pool返却しない。通常outlineではposition用managed配列もArrayPoolも不要。
色付きフォントのadvance取得は既存native計測を維持する。
fillとstrokeは異なるPaintで、RGBA・stroke幅・dash effectを所有者だけが更新する。
effect変更前にPaintから切り離し、旧effectをdisposeする。PNG/SVGの独立呼出しはfresh Paint経路を維持する。

公開API、TFM、依存バージョン、配布構成は変更しない。
単一指定通常フォント・IVS基底文字化・PDF専用・Reader分離を維持する。
収集→subset準備→本描画の2周構成、active page payload最大1を維持する。
ブックモデル・シートgeometry・PDF spoolは引き続き保持するため、全体メモリが定数とは主張しない。

## 再現方法と証拠

[証拠ディレクトリ](validation/core-performance-optimization/)に、各Phaseの累積patch、
共通ハーネスpatch、入力bytes、DLL/フォント/入力hash、環境、実行コマンド、
試行データ、ラウンド別中央値、出力/API比較、検証ログを保存する。
過去のレポートと凍結PDFは更新しない。

各Release runtimeは`artifacts/core-performance/{baseline,phase1,...,phase5}`へ保存する。
再構築時は基準commitの別checkoutへ対応する累積patchと`common-harness.patch`を適用し、
`tools/ExcelRenderer.Core.Validation`をReleaseビルドして出力一式を別directoryにコピーする。
本体比較用に変更前/変更後の`ExcelRenderer.Performance`とToolの出力一式も別保存する。
ビルド・テスト・測定は同時実行しない。

```bash
python3 scripts/measure-core-optimization.py
python3 scripts/validate-core-optimization.py
```

主比較はobserver無効、各caseにつき3組の交互順序ラウンド。
各processの初回をcold、以後3変換をwarmとして分離する。
初期化、font読取、入力読取、最終保存を含むend-to-endを測る。
診断付き実行は別採取し、包含関係のある工程時間を足し合わせない。
本体は既存render-caseのobserverを両版で有効にした別プロトコルで比較する。

ハーネスの割り当て/GC差分は変換直後、検証PDF解析前に確定する。
ピークRSSはprocess全体のhigh-water markであり、前試行の検証PDF解析の影響も含む。
10msサンプルのmanaged最大値は瞬間的なpeakを取り逃す場合がある。
この制約を含め、単一のpool中央値だけで5%退行目標の達成を宣言しない。

## 回帰検証

追加テストは固定seedの日本語/ASCII、サロゲート、結合文字、ZWJ、IVS、欠落glyph、
連続改行/CRLF/末尾空白、長い文字要素、複数サイズ、境界幅の直前/一致/直後を
旧全文候補計測と比較する。行文字列、幅、baseline、行高、glyph、UTF-8、clusterを厳密比較する。
不正UTF-16は旧glyph/cluster検証と同じ結果になることを確認する。
長文はcacheに入らない条件でglyph取得1回・対象文字数Nを確認する。

ページ候補は逆順挿入したモデルで、両PageOrder、タイトル行/列/両方/なし、
非表示行列、境界を跨ぐ結合、画像を含め汎用materializationと比較する。
収集文字列・glyph列は本描画の文字コマンドと比較し、透明文字/サイズ0/縮小/header/footerも含める。
cacheは装飾/幅の共有、wrap/sizeの分離、511/512/513件、保持量上限、
頻出見出しと一意文字の混在を確認する。
描画は実線・点線・破線・鎖線・二重線の交互描画とfillをfresh Paintとpixel比較し、positionも従来の加算順と比較する。
変換時のpayload、非文字コマンド構築回数、native資源生成/解放の対応も確認する。
既存suiteがキャンセル、出力例外、画像失敗、並列/入れ子scope、公開layoutの再描画を検証する。

Windowsでの実行は行っていない。Linux結果をWindows成功として扱わない。

## 必須チェックの実行結果

| チェック | Linux結果 |
| --- | --- |
| solution Debug/Release Rebuild | いずれもwarning 0 / error 0 |
| solution tests | 3047 passed / 0 failed / 0 skipped（Core 449、本体1177、Tool 135、Formulas 833、Mapping 226、OpenXml 227） |
| Core Release coverage対象 | 441 passed、line 84.58% / branch 74.27%。gate 82.12% / 70.08%を通過 |
| Core Release Performance category | 8 passed |
| namespace | 734宣言とcompiler shim 2個を検証 |
| 禁止依存 | source/config 1618件、restore graph 24件、managed AssemblyRef inventory 726件を検証 |
| Reader/Core isolation | 独立consumerのrestore/build/publish、初期output、graph、AssemblyRef、別process実行が成功 |

最初のsolution実行では既存PerformanceTestsの全コマンド構築回数の期待値が旧2周分だったため8件失敗した。
期待値を「本描画の全コマンド構築は1周分、収集用文字payload構築は1周分」へ更新し、
再索引/再ソートの不在とPaint生成/解放の対応も追加した後、上記の全件成功を得た。
PDF内容・診断・ページ数の期待値は変更していない。
最初の並列MSBuildは子nodeが終了したため、以後は`-m:1`でRebuildした。

## 出力・APIの比較結果

- Core: 生成した12入力 × Phase 1〜5の60比較と、既存9入力の最終版比較、合計69比較・2617ページ。
- 本体PDF: 既存9入力・14ページ。
- 本体PNG/SVG: 画像、装飾、罫線、paginationの4入力 × 2形式、合計8比較・16ファイル。
- PDFは同一MuPDF 1.26.6、96 dpiでpixel完全一致。ページ寸法、raw text/座標、drawing、画像情報、linkも一致。
- 埋め込みフォントsubsetのSHA-256も一致。全生成入力で収集font run数が一致し、本描画の未収集glyph検証を削除していない。
- PNG/SVGはbytes完全一致。公開/protected/nullable等の契約はCore 82 entry、本体2361 entryで差分0。

詳細は[出力比較](validation/core-performance-optimization/output-comparison.json)、
[API比較](validation/core-performance-optimization/api-comparison.json)、
[処理量・資源検証](validation/core-performance-optimization/structural-validation.json)を参照。
比較対象PDFは`artifacts/core-performance`に保持し、比較レポートにはpage rasterとfontのhashを保存した。
生成入力と正規化した既存入力の同一bytesも証拠ディレクトリに保存した。

## 性能結果

主比較のwarm値は、各processの初回1変換の後に採取した3変換の中央値。
以下の各行は別ラウンドであり、全試行をpoolしていない。

### 長文折り返し（1000行・2列）

| Round | 変更前 ms | 最終版 ms | 時間短縮 |
| --- | ---: | ---: | ---: |
| 0 | 768.14 | 406.92 | 47.0% |
| 1（逆順） | 820.27 | 383.18 | 53.3% |
| 2 | 760.48 | 388.08 | 49.0% |

割り当て量のラウンド中央値の中央値は440.64 → 213.26 MiB（51.6%削減）。
このケースの入力は各セルで異なる文字列であり、長文単体テストも2048文字を超えてcache対象外。
cache hitだけで得た結果ではない。診断実行では段落glyph取得4000回、対象UTF-16文字数1165780で、
2周分の段落文字数に一致する。通常outlineの確定行では追加GetGlyphsを呼ばない。

### 各Phaseの割り当て量

単位MiB。各ラウンドのwarm割り当て中央値について、その3ラウンドの中央値を表示する。
各試行・各ラウンドの時間/RSS/cold値は[生データ](validation/core-performance-optimization/samples.jsonl)と
[ラウンド別集計](validation/core-performance-optimization/summary.json)に保存している。

| Case | baseline | Phase 1 | Phase 2 | Phase 3 | Phase 4 | Phase 5 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 罫線・背景1000行 | 44.60 | 42.38 | 42.10 | 40.32 | 40.51 | 39.85 |
| 長文折り返し1000行 | 440.64 | 440.14 | 220.20 | 216.13 | 218.54 | 213.26 |
| 同文反復1000行 | 33.50 | 33.16 | 33.15 | 31.06 | 31.06 | 30.75 |
| 画像反復1000行 | 44.30 | 43.89 | 43.74 | 41.66 | 41.98 | 41.49 |

Phase 4は有界保持とkey共有を目的とし、一意文字列で割り当て量を必ず減らす対策ではない。
CLOCK entryの管理コストがあり、長文一意ケースはPhase 3より約1.1%増えた。
一方で保持量を131072単位に制限し、全件Clearによる頻出値の一斉消失を避ける。
装飾/幅の共有、511/512/513件、長文上限、頻出見出しのhit、保持上限は別途テストで確認した。
無制限なentry数やglyph保持は追加していない。

### 処理量・native資源

診断は主比較と別実行であり、診断付き時間を主比較へ混ぜていない。
旧版には[観測専用patch](validation/core-performance-optimization/baseline-diagnostics.patch)を適用した別runtimeも作り、
[旧版処理回数](validation/core-performance-optimization/baseline-observations.json)を補測した。
このruntimeのpixel・文字座標・subsetが、無変更baselineと同じであることも確認した。
観測専用runtimeは主比較・追加速度測定に使用していない。

| 処理回数 | 旧版の観測専用実行 | 最終版の診断実行 |
| --- | ---: | ---: |
| 折り返しcaseのlayout GetGlyphs | 1141780 | 4000 |
| 同・計測対象UTF-16文字数 | 11469880 | 1165780 |
| 同・managed position配列 | 38000 | 0 |
| 同・描画position要素数 | 558890 | 558890 |
| 罫線caseのPaint生成 | 5100 | 2 |
| 罫線caseのページ内BandIndex構築 | 50 | 0 |
| 罫線caseの後段重複ソート | 50 | 0 |
| 収集周の背景コマンド（罫線case） | 2000 | 0 |
| 収集周の画像コマンド（画像case） | 100 | 0 |

旧版と最終版の折り返しcaseのcache miss数はともに4000であり、glyph処理量削減をcache hitへ帰属させていない。
最終版のpositionは同じ要素数をnative Spanへ書き込む。最終版の収集用列挙はDrawTextCommandだけを返す。


- 罫線case 25ページ: 全描画コマンド構築50 → 25回、罫線候補16384 → 8192。
  収集周は文字だけを逐次生成するため、背景/画像コマンドと罫線競合解決は発生しない。
- plan経路のページ内BandIndex再構築・重複ソートは0。独立layoutの汎用経路は残している。
- 全12入力でactive page payload最大1、収集font run数一致。
- 最終版のPaint生成は罫線caseで2、折り返しcaseで1。画像だけ/空白では未生成。
  各入力でpaint/effect/typeface/pdf font/subset typefaceの生成数と解放数が一致。
- 通常outlineのPDF position用managed配列を廃止。builder-owned Spanへ直接書き込む。
- 診断でcache最大512 entry、最大131070保持単位。テストでも131072上限を確認。

### 5%を超えた時間/RSSの調査

主比較には、小規模・罫線・画像・本体出力の一部で5%を超える時間/RSS悪化があった。
全件を合格扱いにせず、同じ入力bytes/DLLを使い、tiered compilation無効・warm 8変換・
3組の交互順序ラウンドで追加調査した。本体は主比較で時間またはRSSが5%を超えた全組合せを再測定した。

- 罫線caseの追加測定は時間比0.949 / 0.868 / 0.811、RSS比0.980 / 0.981 / 1.000。
  主比較の時間悪化はこの設定では再現せず、割り当て削減は両設定で確認した。
- 10000行caseの追加測定は時間比0.970 / 0.952 / 0.931。5000行は0.977 / 0.957 / 1.004。
- 折り返しcaseの追加測定は時間比0.335 / 0.280 / 0.329。主比較とは設定が異なるため、
  この値へ主比較の改善率を差し替えていない。
- 画像だけの追加測定は時間比1.196 / 0.861 / 1.254。
  ヘッダーだけは2.119 / 1.576 / 1.011だが、主比較では0.907 / 0.993 / 0.847だった。
  小規模ケースは設定・ラウンドで方向が変わり、改善/非退行を確定できない。
- 本体02-image PDFの追加測定は時間比0.980 / 1.005 / 1.094、RSS比1.046 / 0.994 / 1.068。
  本体06-layout-and-pagination PNGは時間比1.044 / 0.974 / 1.006、RSS比0.984 / 1.070 / 0.995。
  一部ラウンドの5%超過は追加測定でも残る。

最大RSSにはハーネスのPDF検証も含まれ、GC/native allocator/実行環境の影響を分離できていない。
悪化原因をJITだけと断定しない。**全ケースで時間・ピークRSSの悪化が5%以内という目標は、この環境では未確認。**
単一のpool中央値や、改善したラウンドだけを選んで合格にはしていない。

[主比較・追加測定の全ラウンド集計](validation/core-performance-optimization/analysis.json)、
[追加Core生データ](validation/core-performance-optimization/controlled-samples.jsonl)、
[残りCore/本体の再測定](validation/core-performance-optimization/regression-rechecks.jsonl)、
[本体主比較](validation/core-performance-optimization/main-samples.json)を保持する。
追加測定と集計の再現コマンド:

```bash
python3 scripts/recheck-core-optimization.py
python3 scripts/recheck-core-optimization-regressions.py
python3 scripts/summarize-core-optimization.py
```

## 完了状態と未検証

| 項目 | 状態 |
| --- | --- |
| 1 収集専用文字経路 | 実装済み・回帰/処理量/割り当て検証済み |
| 2 線形折り返し・glyph引き継ぎ | 実装済み・厳密差分/処理量/性能検証済み |
| 3 確定候補の受け渡し | 実装済み・順序/selection/割り当て検証済み |
| 4 有界CLOCK・key改善 | 実装済み・共有条件/件数/保持量/管理コスト検証済み |
| 5 Paint・position buffer | 実装済み・pixel/座標/資源数/割り当て検証済み |
| 公開API・Reader分離・既存出力 | 比較・静的検証・独立consumerが成功 |
| 時間/RSSの全ケース5%非退行 | 未確認。上記の追加調査と生データを参照 |
| Windows | 未実行。Linux結果から成功と推定しない |

SKTextBlobBuilder/blobのcacheは任意項目のため採用していない。
原文/glyph/cluster/size/faceを含む追加保持やnative寿命の複雑化を避け、必須のPaintとposition削減を実装した。
