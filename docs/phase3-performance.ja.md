# フェーズ3：ページ単位生成と資源の省メモリ化

2026-10-07、比較元は1.7.1 `8f73fbdce043668f07718a08611d35312313f059`。
実帳票は提供されていないため、利用者測定の519MBとの直接比較はしていない。
旧5GB版との比較でもない。

文字あり10,000行では、PDFは19.20→10.90秒／395.0→369.4MiB、
ページ別PNGは33.03→13.03秒／337.1→292.2MiB、
連続SVGは76.10→77.20秒／3,728.8→292.7MiBだった。
全形式でpayload最大1を確認したが、peak RSSが必ず減るわけではない。
ページ別SVGの1,000／5,000行は約9%増えた。時間10%目標を超える条件も残る。

## 実装と保持範囲

converterは幾何・ページbandの計画、選択ページの事前確認、ページ単位の出力を順に行う。
事前確認はtrim・空内容・PNG寸法・リンク・診断policyをsink.Open前に確定する。
選択ページを2回生成し、保持するRenderPage/commands payloadは最大1個。
公開Layout/LayoutContinuousと直接rendererのsignatureは互換wrapperとして残した。
Markdownのモデル・出力経路は変更していない。

連続画像はシートごとに背景、セル罫線、結合外周、文字、Z順の画像・図形を再走査して生成する。
全文字layout辞書や連続commands配列は保持しない。1シート＝1画像、PNGは単一bitmapのまま。

SVGの中間bufferは既定8MiBを超えるwrite前に専用tempへ移る。
入力spoolは既存WorkbookInputOptionsによる独立した方針であり、byte[]へ戻さない。
選択外の本文・画像bytes・図形文字モデルは生成せず、名前・結合・原点幾何・診断用metadataを残す。
対象シートのmissing-glyph診断後に描画範囲の投影を行う。
セルと画像・図形の軽量band索引は計画で共有し、page context・文字layoutを保持しない。
ヘッダー／フッターの日時は変換内のclock snapshotを両周で共有する。
OSフォントのbytesとdigestは初回選択時に読み、明示登録は従来通り事前snapshot・検証する。
画像は元byte[] identityとnative種類をkeyに、conversion内の64MiB推定上限LRUとactive leaseで共有する。
PDFsharpが文書に保持する画像資源はこのcache上限とは別。

## 測定方法

Debian 13 Linux、AMD EPYC 9V74（2 CPU quota、8GiB memory limit）、.NET 10.0.401 SDK／10.0.12 runtime、Release、同一コンテナ・同一フォント・同一fixture。
第三者fontは同梱のNoto Sans JPを明示登録し、主比較はOS font/optional packを無効にした。
20列、行高18pt、列幅10、背景とthin/double罫線、文字ありは `Report ABC 123`。
PNGは24 DPI。出力はCountingStreamを通したStream.Nullで、ディスク出力差を除いた。
SVGでも実際にSkia描画・XML正規化・全出力bytesの書込みを行う。

各条件3回の新規プロセスを直列実行し、中央値を示す。baselineは固定コミットのライブラリに同じharnessをコピーしたもの。
最終版の変更後に新実装側の全条件を再測定し、途中の測定値は提出データから除いた。
peak RSSはProcess.PeakWorkingSet64、private/managedは50msサンプルの最大と終了時値をraw JSONに収録した。
RSS−GC.GetTotalMemoryの差はnative専用値ではなく、runtime・managed heapの余白等を含む残差推定。
同一プロセス10回は別測定で、harnessだけが変換終了後にGCを行う。製品コードにGC.Collectはない。

事前確認・再生成は `pagePreflight.ms` / `pageBuild.ms` に記録する。
連続の遅延生成はMoveNext時間もpageBuildへ加算する。計測observerの負荷を含む。
`fontLockWait.ms`、`pdfFinalSave.ms`、`pngEncode.ms`、`svgNormalize.ms` は別記録。
新旧の命令数・GCタイミングが異なるため、終了時managedだけをpeakの代用にしない。

[主比較raw](phase3-results/raw.jsonl)、[中央値JSON](phase3-results/summary.json)、
[補足raw](phase3-results/supplement.jsonl)、[50回raw](phase3-results/retention-extended.jsonl)、
[環境・fixture hash](phase3-results/environment.json)、[package notice/license hash](phase3-results/packages.json)。
途中の試行と最終版の数値は混在させていない。

## 再現

```bash
git worktree add --detach /tmp/excel-baseline 8f73fbdce043668f07718a08611d35312313f059
# baselineのライブラリは変更せず、同じ公開APIの計測harnessだけを配置する。
cp tools/ExcelRenderer.Performance/{Program,RenderMemoryBenchmark,InputSpoolBenchmark}.cs /tmp/excel-baseline/tools/ExcelRenderer.Performance/
dotnet build tools/ExcelRenderer.Performance/ExcelRenderer.Performance.csproj -c Release
(cd /tmp/excel-baseline && dotnet build tools/ExcelRenderer.Performance/ExcelRenderer.Performance.csproj -c Release)
python tools/ExcelRenderer.Performance/compare_phase3.py --baseline /tmp/excel-baseline --current "$PWD" --output /tmp/phase3-results
# optional packの比較は両harnessの隣に同じExcelRenderer.Fonts.dllを置く。
python tools/ExcelRenderer.Performance/compare_phase3_supplement.py --baseline /tmp/excel-baseline --current "$PWD" --output /tmp/phase3-results
```

生成器はlatin/empty/images/mixed/merges/wrap/far/rowstyle等を受け付ける。
追加の保持測定は `render-case /tmp/phase3-results/latin-100.xlsx pdf paginated 50`
および `svg paginated 50` を両rootで実行する。
集計は `python tools/ExcelRenderer.Performance/summarize_phase3.py /tmp/phase3-results`。

入力spoolのZIPはseed7241、非圧縮32MiBで、ClosedXMLを混ぜずに入力準備だけを比較する。

## 新規プロセス3回の中央値

時間は秒、メモリはMiB。文字あり=latin、罫線のみ=empty。P=ページ別、C=連続。

| 行数 | 内容 | 形式 | 枚数 | 時間 old→new | 時間差 | peak RSS old→new | RSS差 |
|---:|---|---|---:|---:|---:|---:|---:|
| 1,000 | latin | Pdf/P | 78 | 2.67→2.68 | +0.5% | 150.5→147.8 | -1.8% |
| 1,000 | latin | Png/P | 78 | 3.15→2.93 | -7.1% | 140.4→132.4 | -5.7% |
| 1,000 | latin | Svg/P | 78 | 9.62→9.98 | +3.7% | 163.3→178.2 | +9.1% |
| 1,000 | latin | Png/C | 1 | 2.48→2.60 | +4.9% | 147.8→135.7 | -8.2% |
| 1,000 | latin | Svg/C | 1 | 9.40→10.35 | +10.1% | 578.9→139.0 | -76.0% |
| 1,000 | empty | Pdf/P | 78 | 2.30→2.23 | -2.9% | 134.9→129.0 | -4.4% |
| 1,000 | empty | Png/P | 78 | 2.64→2.41 | -8.7% | 133.1→129.6 | -2.7% |
| 1,000 | empty | Svg/P | 78 | 2.94→3.03 | +3.1% | 131.4→138.2 | +5.2% |
| 1,000 | empty | Png/C | 1 | 2.03→2.04 | +0.8% | 141.1→136.5 | -3.3% |
| 1,000 | empty | Svg/C | 1 | 2.59→2.63 | +1.3% | 148.7→139.9 | -6.0% |
| 5,000 | latin | Pdf/P | 387 | 9.54→8.82 | -7.6% | 286.7→267.5 | -6.7% |
| 5,000 | latin | Png/P | 387 | 10.28→9.10 | -11.5% | 224.9→199.7 | -11.2% |
| 5,000 | latin | Svg/P | 387 | 42.75→42.12 | -1.5% | 270.4→293.9 | +8.7% |
| 5,000 | latin | Png/C | 1 | 8.00→8.90 | +11.3% | 247.4→225.7 | -8.8% |
| 5,000 | latin | Svg/C | 1 | 39.76→40.50 | +1.9% | 2133.5→203.0 | -90.5% |
| 5,000 | empty | Pdf/P | 387 | 8.26→7.34 | -11.1% | 228.9→185.3 | -19.1% |
| 5,000 | empty | Png/P | 387 | 9.30→7.89 | -15.1% | 218.4→179.5 | -17.8% |
| 5,000 | empty | Svg/P | 387 | 9.42→8.94 | -5.1% | 217.8→184.1 | -15.5% |
| 5,000 | empty | Png/C | 1 | 6.54→7.24 | +10.7% | 250.9→216.7 | -13.6% |
| 5,000 | empty | Svg/C | 1 | 6.73→7.62 | +13.2% | 285.0→176.1 | -38.2% |
| 10,000 | latin | Pdf/P | 771 | 19.20→10.90 | -43.2% | 395.0→369.4 | -6.5% |
| 10,000 | latin | Png/P | 771 | 33.03→13.03 | -60.6% | 337.1→292.2 | -13.3% |
| 10,000 | latin | Svg/P | 771 | 99.08→79.94 | -19.3% | 349.0→341.1 | -2.3% |
| 10,000 | latin | Png/C | 1 | 16.43→16.12 | -1.9% | 369.3→309.5 | -16.2% |
| 10,000 | latin | Svg/C | 1 | 76.10→77.20 | +1.4% | 3728.8→292.7 | -92.2% |
| 10,000 | empty | Pdf/P | 771 | 17.13→9.70 | -43.4% | 278.8→258.3 | -7.4% |
| 10,000 | empty | Png/P | 771 | 31.40→11.34 | -63.9% | 282.6→258.3 | -8.6% |
| 10,000 | empty | Svg/P | 771 | 20.69→12.45 | -39.9% | 291.3→256.5 | -12.0% |
| 10,000 | empty | Png/C | 1 | 13.07→13.35 | +2.2% | 358.0→322.0 | -10.1% |
| 10,000 | empty | Svg/C | 1 | 9.94→11.15 | +12.2% | 423.6→235.6 | -44.4% |

## フェーズ3の内訳（文字あり）

preflightはbuildの1周目を含み、両列は加算しない。buildは両周の合計。private/managedは50msサンプル最大の中央値。

| 行数 | 形式 | preflight ms | build ms | Save/encode/XML ms | peak private MiB | peak managed MiB | PNG画素 MiB | SVG buffer MiB | spill |
|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 1,000 | Pdf/P | 255 | 431 | 62 | 186.6 | 61.1 | 0.00 | 0.00 | 0 |
| 1,000 | Png/P | 256 | 416 | 172 | 170.7 | 39.5 | 0.21 | 0.00 | 0 |
| 1,000 | Svg/P | 268 | 356 | 900 | 223.0 | 75.1 | 0.00 | 4.00 | 0 |
| 1,000 | Png/C | 8 | 204 | 97 | 175.3 | 40.1 | 9.84 | 0.00 | 0 |
| 1,000 | Svg/C | 9 | 170 | 998 | 174.0 | 39.1 | 0.00 | 8.00 | 1 |
| 5,000 | Pdf/P | 932 | 1198 | 219 | 294.5 | 167.1 | 0.00 | 0.00 | 0 |
| 5,000 | Png/P | 882 | 1128 | 846 | 252.2 | 111.9 | 0.21 | 0.00 | 0 |
| 5,000 | Svg/P | 901 | 1126 | 2377 | 344.6 | 201.6 | 0.00 | 4.00 | 0 |
| 5,000 | Png/C | 7 | 298 | 466 | 263.7 | 112.0 | 49.21 | 0.00 | 0 |
| 5,000 | Svg/C | 8 | 317 | 2228 | 253.2 | 112.0 | 0.00 | 8.00 | 1 |
| 10,000 | Pdf/P | 1147 | 1558 | 422 | 402.1 | 248.0 | 0.00 | 0.00 | 0 |
| 10,000 | Png/P | 1040 | 1457 | 1666 | 336.1 | 203.6 | 0.21 | 0.00 | 0 |
| 10,000 | Svg/P | 1065 | 1557 | 4432 | 391.6 | 238.8 | 0.00 | 4.00 | 0 |
| 10,000 | Png/C | 8 | 467 | 931 | 352.5 | 207.3 | 98.42 | 0.00 | 0 |
| 10,000 | Svg/C | 7 | 523 | 4376 | 338.2 | 201.3 | 0.00 | 8.00 | 1 |

## 補足の中央値

| 条件 | 時間 old→new 秒 | peak RSS old→new MiB | decode/hit new | 読込font bytes new |
|---|---:|---:|---:|---:|
| images-pdf-paginated | 5.60→3.14 | 175.2→160.6 | 1/25 | 5472784 |
| images-png-paginated | 2.96→2.85 | 147.4→138.4 | 1/25 | 5472784 |
| images-svg-paginated | 10.13→10.30 | 162.1→181.2 | 1/25 | 5472784 |
| images-png-continuous | 2.63→2.65 | 150.7→147.0 | 1/0 | 5472784 |
| images-svg-continuous | 8.89→9.13 | 570.0→146.2 | 1/0 | 5472784 |
| selection-one-of-many | 27.60→6.80 | 285.8→292.1 | 0/0 | 5472784 |
| system-fonts | 1.50→1.42 | 191.9→119.4 | 0/0 | 5472784 |
| font-pack | 1.55→1.34 | 357.9→120.2 | 0/0 | 5472784 |
| mixed-print-areas-links | 1.48→1.52 | 125.5→126.7 | 1/2 | 5472784 |
| input-spool | 0.08→0.03 | 111.5→29.7 | 0/0 | 0 |

## 同一プロセス10回

100行の文字fixture。変換後のharness GC後のRSS/managed。初回から10回目の増加と、2〜10回目の範囲を分ける。

| 条件 | revision | RSS 初回→10回目 MiB | 2〜10回目のRSS範囲 MiB | managed 初回→10回目 MiB |
|---|---|---:|---:|---:|
| pdf-paginated | baseline | 117.8→138.2 | 129.5–138.2 | 17.9→18.2 |
| pdf-paginated | phase3 | 117.7→146.4 | 126.0–146.4 | 17.9→18.6 |
| png-paginated | baseline | 114.6→145.2 | 135.3–145.2 | 17.9→18.2 |
| png-paginated | phase3 | 118.0→140.5 | 126.4–140.5 | 17.9→18.2 |
| svg-paginated | baseline | 129.7→173.4 | 140.7–173.4 | 18.0→18.2 |
| svg-paginated | phase3 | 130.0→170.3 | 135.2–170.3 | 17.9→17.9 |
| png-continuous | baseline | 118.4→141.8 | 132.5–141.8 | 17.9→18.2 |
| png-continuous | phase3 | 112.9→142.9 | 130.6–142.9 | 17.5→17.7 |
| svg-continuous | baseline | 155.9→196.8 | 182.9–196.8 | 18.0→18.2 |
| svg-continuous | phase3 | 122.7→152.6 | 138.6–152.6 | 17.5→17.5 |



## 50回の追加保持測定

10回ではRSSの暖機と保持を区別し切れなかったため、PDF/SVGのページ別出力を50回まで延長した。
managedはほぼ横ばいだが、RSS/privateは後半でも増える区間がある。native handle解放とcache上限のテストは通っているが、RSSの増加がallocator/JIT/managed heap余白か長期保持かはこの計測だけでは確定できない。長期native/RSS保持の無増加は未確認事項として残す。

| 形式 | revision | RSS 1/10/20/30/40/50回 MiB | private 1/50回 MiB | managed 1/50回 MiB |
|---|---|---|---:|---:|
| pdf | baseline | 113.9/143.8/149.0/152.1/156.4/156.9 | 149.9→185.1 | 18.0→18.5 |
| pdf | phase3 | 117.0/138.4/143.8/149.3/153.1/159.9 | 153.7→188.2 | 17.9→18.6 |
| svg | baseline | 128.6/173.5/174.7/176.3/180.4/180.4 | 166.8→209.6 | 17.9→18.2 |
| svg | phase3 | 132.0/166.5/167.7/167.9/174.0/190.8 | 169.1→219.3 | 17.9→18.1 |

## 時間目標と残るtradeoff

時間増が10%を超えたのは、1,000行文字あり連続SVG（+10.1%）、
5,000行文字あり連続PNG（+11.3%）、5,000行罫線のみ連続PNG（+10.7%）、
5,000行罫線のみ連続SVG（+13.2%）、10,000行罫線のみ連続SVG（+12.2%）。
未trim・非rangeの連続出力では、事前確認を先頭commandの有無確認まで削減した。
これらのpreflightは中央値7〜9msで、主な時間増をこれ以上のpreflight削減だけで解消できない。
背景・罫線は全文字計測を避け、空文字と結合外周のないセルも該当層で計測しない。
メモリ内入力のread cursorも、seek/restore wrapperから共有readonly MemoryStream viewへ変更済み。

5,000行文字あり連続PNGのreaderは4,085→4,781ms、font初期化は17→328ms、
新しいbuild297.9ms・encode465.9ms・preflight7.4msだった。
10,000行罫線のみ連続SVGはreader5,099→5,560ms、font初期化15→315ms、
build170.9ms・XML745.8ms・preflight7.0msだった。
明示fontのnative検証を前倒ししているため、font時間の差だけを総時間の増加と同一視しない。
各フェーズ中央値を合計して総時間中央値の因果内訳にもしていない。
出力中のnative描画、GC、temp I/O、readerの差はrawから再調査できる。
今回は定メモリ保証や全条件の時間改善を主張せず、この時間目標の未達を残る課題として記録する。

## 補足の読み方

反復画像は1024×1024 PNGを元byte[]として使い、26ページのclipでdecode 1回／hit 25回を確認した。
SKImageのcache推定は4MiB、PDFのXImageは4,200,835 bytes。64MiB上限・evict・超過・active leaseは構造テストで確認する。
非選択シートのbodyは0 cellsとなり、source index・名前・merge・原点幾何を保持するreader投影テストが通った。
少数ページ選択は時間27.60→6.80秒に減ったが、reader peakが残りRSSは285.8→292.1MiBだった。
OS font／packのケースは100行で、必要bytesは5,472,784のみ。OS font初回以前の完全snapshotを保証しない。
入力32MiB ZIPの独立測定は準備処理のみで、ClosedXMLや出力を含まない。
PNG/SVGで同じ元dataのcropが違ってもcacheを共有し、native leaseを保持中はdisposeしない。

## 資源の下限と次の改善

| 領域 | 今回残るもの |
|---|---|
| reader | ClosedXMLは全ブックを読む。SAX読込は未実装。選択対象ReportCellモデルは残る |
| geometry | 行列override、セルaddress/bounds索引、選択ページmetadataは入力・ページ数に比例する |
| PDF | 最終PdfDocumentとPDFsharp文書資源をSaveまで保持。Save1回、Import0回 |
| PNG | 現在ページ/シートのRGBA bitmap＋row alignment＋encoder。100 million pixelsは画素だけで約381MiB |
| SVG | アプリ側bufferは8MiB上限、超過分はtemp。SKSvgCanvas内部native資源は別 |
| font | 選ばれたbytesとconversion内native資源。OS fontは初回読込前に完全snapshotではない |
| image | cache推定最大64MiB。単独超過画像・active lease・PDF文書資源は別に存在し得る |

scanline PNG、タイル画像分割、OpenXML SAX、並列生成、commandsのserialization、
process-global ResolvedFontDataの寿命変更は実装していない。
重い単一native操作を即時キャンセルできない制約も残る。

## 検証

Rebuildは0 warnings/0 errors。ライブラリ379件、CLI32件、合計411件が通過。
ページpayload observer、文字計測数、PNG画素、SVG構造、PDFリンク/描画probe、
複数印刷範囲、trim、範囲を跨ぐ結合、描画順、glyph/IVS/emoji、Markdown、
spill境界/巨大write/seek/no-temp/失敗cleanup、PNG寸法、input上限/nonseekable/cancel、
画像lease/evict/超過、選択外metadata、font遅延snapshot、既存直接APIを検証した。
Skia native callbackの書込み例外はmanagedへ戻ってから再throwし、プロセス終了を防ぐ。
caller所有streamは閉じない。

このLinux環境では既存のgeneric font解決テスト3件がbaselineでも失敗したため、
DejaVu Sansの明示[fontconfig](phase3-results/fontconfig.xml)を指定して全件実行した。
そのdir/cachedirは実行環境に合わせる。主性能比較は明示fontを用いるためこのaliasに依存しない。フォント許容差は変更していない。
3パッケージのRelease packも成功し、font/license・project/package構成に変更はない。
実帳票・別OSでのpeak RSSと見た目は未検証。合成測定の改善率を519MBへ外挿しない。
