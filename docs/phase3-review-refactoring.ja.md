# フェーズ3レビュー後の保守性改善

## 変更と責務

比較元はフェーズ3の `800e1f7738dd0ee61110312ff3e7b048d056d1b1`、修正後のコードは `4b6c477`。1.7.1との性能比較を今回の非回帰判定には使っていない。

1. **幾何準備の境界**：工程の先頭6件を取る処理を廃止し、行列・選択範囲の幾何を準備する6工程を `geometryPasses` に明記。文字計測とセル配置はページ生成側、ページband計算は計画側に置く。
2. **セルのページ対象判定**：文字計測前と配置前で重複していた判定を `PageCellSelection` に集約。半開区間、明示範囲での結合交差、タイトル反復のepsilonを一箇所で扱う。候補band索引・重複排除・元順序は維持し、タイトルmembershipのsetは計画/Builderで再利用する。
3. **連続描画の層**：数字と自由な `measureText` 引数を廃止し、`DrawingLayer` と明示順序を導入。全生成と層別生成は別メソッドにし、文字計測は全生成またはText層でのみ行う。不正enum値は入口で拒否する。
4. **ページ生成元の整合性**：`SheetPage` はprivate constructorとページ別/連続/空fallbackのfactoryで生成。plan/index/countはget-only、連続かどうかはcanvas planから導出する。descriptor/viewport/linksのrecord更新では生成元を保持する。複数PrintAreasを通したsource番号とarea内indexは区別して検証する。

4点はそれぞれ別コミットに分けた。公開API、CLI、manifest、metricsの形式、事前確認と描画の2周、SVG spill、画像LRU/lease、resource session、入力spoolは変更していない。

## 検証

```bash
dotnet restore ExcelRenderer.slnx
dotnet build ExcelRenderer.slnx -t:Rebuild --no-restore --verbosity:minimal
FONTCONFIG_FILE="$PWD/docs/phase3-results/fontconfig.xml" dotnet test ExcelRenderer.slnx
```

Rebuildは0 warnings / 0 errors。テストはlibrary 400件、CLI 32件、合計432件が成功した。既存レポートと同じFontconfig条件を再現し、期待値・フォント許容差は緩めていない。project/package構成と同梱font/licenseは変更していないため、今回の追加packは行っていない。

追加した検証は、固定期待の開始/終了境界、結合交差/接触、通常結合、行/列タイトル、epsilon前後、タイトルなし、層別の文字計測回数、不正enum値、全生成時の空文字結合罫線、空/ヘッダーのみのページ番号とcanvas寸法。複数PrintAreasと結合タイトルを含むfixtureで公開Layoutの文字/罫線位置とconverter PNGのdecode画素を比較した。連続fixtureは隣接背景と文字の重なりに、結合外周と画像/図形のZ順を加え、PNG画素とSVG構造の一致を確認した。既存のtrim/リンク/選択/資源解放/直接renderer/CLI回帰も全件実行した。

## 性能比較の再現方法

```bash
FONTCONFIG_FILE="$PWD/docs/phase3-results/fontconfig.xml" \
  python3 tools/ExcelRenderer.Performance/compare_phase3_review.py \
  --baseline 800e1f7738dd0ee61110312ff3e7b048d056d1b1 \
  --current 4b6c477 --output /tmp/phase3-review-comparison
```

同一環境で、20列・文字あり1,000行のページ別PDF/PNG/SVG、連続PNG/SVGと、10,000行の連続SVGを各3回の新規プロセスで測定する。前後を交互に起動し、並行変換は行わない。Release、24DPI、Noto Sans JPの明示登録、OS font/同梱pack無効、CountingStream経由のStream.Null出力を共通条件とする。

scriptは指定commitを一時コピーしてRelease buildする。文字計測呼出数と最終PDF documentページ数を数える同一の計器を両コピーにのみ挿入する。製品ソース・metrics形式には追加しない。文字計測呼出数はTextMeasurePassが計測serviceを呼んだ回数であり、その内部の有界layout cacheのmiss数ではない。時間とRSSは中央値と3回のmin/maxを示す。

## 性能結果

秒とMiB。括弧内は各3回のmin–max。Pはページ別、Cは連続。

| 行数・形式 | 時間 前→後 | 時間差 | peak RSS 前→後 | RSS差 |
|---|---:|---:|---:|---:|
| 1,000 PDF P | 2.38 (2.35–2.74) → 2.46 (2.40–2.53) | +3.2% | 146.32 (145.88–146.37) → 145.52 (145.17–146.18) | -0.5% |
| 1,000 PNG P | 2.51 (2.51–2.56) → 2.54 (2.50–2.56) | +0.9% | 129.88 (129.82–131.68) → 129.93 (129.84–129.96) | +0.0% |
| 1,000 SVG P | 9.39 (9.27–9.43) → 9.48 (8.84–9.61) | +1.0% | 174.88 (163.71–201.20) → 173.21 (164.94–184.05) | -1.0% |
| 1,000 PNG C | 2.20 (2.08–2.22) → 2.04 (2.02–2.09) | -7.6% | 135.69 (134.08–135.79) → 134.68 (131.98–136.26) | -0.7% |
| 1,000 SVG C | 9.35 (9.08–9.43) → 9.22 (9.10–9.40) | -1.4% | 137.23 (137.04–137.71) → 137.55 (137.25–138.61) | +0.2% |
| 10,000 SVG C | 74.71 (74.44–76.05) → 75.52 (74.74–76.37) | +1.1% | 290.09 (289.78–295.40) → 290.61 (287.81–313.35) | +0.2% |

全6条件で時間/RSSの中央値に10%超の悪化はなかった。前後ともpagePayloadMax=1。ページ別の文字計測呼出数は1,000行で40,000、連続は1,000行で20,000、10,000行で200,000で、各3回とも同数だった。ページ数はページ別78、連続1で一致した。PDFは最終document 78ページ、Save=1を各回確認し、既存のImportを使わない組立経路は変更していない。

SVGのアプリ側memory bufferは全回8MiB以下。連続SVGは前後ともspill=1。10,000行の中間SVGは2,031,067,531 bytesであり、メモリ上限の代わりに十分な一時ディスク容量が必要になる。今回のRSS差は新たな性能向上の根拠にはしない。特にページ別SVGのRSS範囲は前163.71–201.20MiB、後164.94–184.05MiBであり、3回のばらつきも大きい。

初期の10,000行比較では修正前側が終了し、その試行は比較値に含めなかった。初回stderrが未保存のため終了原因そのものは断定しないが、比較用コピーに未使用の他OS向けnative資産まで含めたときの空き約1.8GiBでは中間SVGの必要量を満たさない構成を確認した。修正前側の単独再実行は成功し、比較用コピーの未使用native資産を除いた後に前後各3回が成功した。再現scriptではscratch copiesのみ同じ整理を行い、エラー時のstdout/stderrも保存する。製品のbuffer/一時ファイル設定は変更していない。

計36回の成功測定の[生データ](phase3-review-results/raw.jsonl)、[中央値・min/max・構造指標](phase3-review-results/summary.json)、[環境と比較commit](phase3-review-results/environment.json)を添付する。再現scriptの動作確認環境はPython 3.12.14 / .NET SDK 10.0.401 / Linux x64。大サイズの比較では少なくとも中間SVG約1.9GiBとscratch build用の一時領域を確保する。

## 残る制約

今回の目的は保守性の改善であり、新たな性能向上を主張しない。ClosedXMLの全ブック読込、PDFsharpの最終document、連続PNGの単一bitmap、Skia内部メモリは残る。実帳票未検証と長期RSS増加の原因未確定は[前回レポート](phase3-performance.ja.md)の未解決事項として維持する。
