# PDF省メモリ・高速化の実装と検証

調査基準は [c183be7375c9c301de785dc4584b5cf0c0a2e951](https://github.com/akrym1582/ExcelRenderer/tree/c183be7375c9c301de785dc4584b5cf0c0a2e951)。
実装開始時のHEADもこのコミットで、作業ツリーに既存変更はなかった。測定日：2026-10-07。

実帳票は未提供。以下は機密情報を含まない合成入力での結果であり、利用者報告の「5GB問題解消」を実帳票で確認した結果ではない。

## 実装

- [PDF変換](../src/ExcelRenderer/ExcelConverter.RenderAsync.cs)：一つの文書とrendererへdescriptor単位で直接ページ追加、最終Save=1、Import=0。空ページ、用紙サイズ、trim/viewport、リンクの後書き、画像診断のシート・source page、キャンセルとsink契約を維持。
- [フォント資源](../src/ExcelRenderer/Fonts/ConversionFontResources.cs)：FaceId単位のnative face、face×sizeの行メトリクスと物理faceに結び付いたXFontを変換内で共有。PNG/SVG・絵文字もleaseを通じて借用。成功・例外・キャンセルで所有者が解放。公開IFontManagerの必須メンバーは変更しない。
- FontManagerの支持判定は書記素全体をkeyとして正負ともcache（8,192件、256 UTF-16単位まで）。解決済みrunは512件・文字列2,048単位まで。Registerでrun cacheを無効化。converter内layout cacheも512件・2,048単位まで、font/width/wrap/登録世代を含むkey。不変の内部コレクションのみ等倍共有する。
- 診断のResolveTextRunsを1回に統合。既存の改行測定は維持し、文字幅の単純加算や禁則処理の変更は行っていない。
- [座標](../src/ExcelRenderer/Layout/SheetGeometry.cs)：sorted override・累積差分・二分探索。ゼロ寸法、hidden連続、半開区間を維持。
- [ページ構築](../src/ExcelRenderer/Layout/RenderPageBuilder.cs)：セルの区間索引、タイトルHashSet、コマンドの一括Lookup。長い結合の交差も候補に含める。画像・図形のsource/visual boundsは1回計算して索引化し、元のZ順・clip・rotationを維持。
- [結合検索](../src/ExcelRenderer/Excel/CellRangeIndex.cs)：存在する行・列のみ範囲検索。罫線収集と削除で同じ候補を使い、外周のみ保持。
- workbook内でFontStyle/BorderSide/BorderStyle/CellStyle（結合外周を含む）を値intern。Generalのデータ型別配置とtheme/tint解決後の値を保持。scaled style/borderもbuilder内で再利用。空文字の文字layout/sizeを省き、描画しない行の文字計測を省く。多重印刷範囲でsheet geometryと計測serviceを共有。

10回変換の予備測定でmanaged heapは安定した一方、RSSにnative保持の傾向があったため、フェーズ3の条件付き対応として、変換終了時に未使用のSkia strike cacheをpurgeした。これはプロセス共通のキャッシュを対象とし、他のSkia利用コードの未使用strikeも解放する。使用中フォントの有効性を回帰テストで検証済み。フォント選択やcacheの上限設定は変更せず、通常変換に強制GCは追加していない。

## 測定条件

Debian 13 / .NET SDK 10.0.401 / runtime 10.0.12、CPU割当2、container上限8GiB。
Release、デバッガなし。Noto Sans JP RegularのSHA256：
`D930D5D52D15231C283089760F84584272AD5E37E14607BA0D19C798E7A9CAEC`。
AllowSystemFonts=false、UseFontPack=false、明示登録とfallback=Noto Sans JP、PDF・96dpiのdescriptor。製品の既定値は変更しない。

成功ケースは新規プロセス3回。時間・累積managed割当は中央値、RSSは10ms外部サンプリングの最大値。PDF解析とfixture準備は変換時間外。プロセスpeakにはハーネスの初期化・PDF解析も含む。PrivateMemorySize64とsmapsのprivate residentは別の指標。共有VMのため時間にはばらつきがあり、差の小さいフェーズ1/2の時間差を性能保証として扱わない。

[raw JSONL、サンプルピーク、集計](../tools/ExcelRenderer.Performance/results/)には、入力/font SHA、ZIP entry圧縮・展開サイズ、cell/row/col/style数、印刷範囲、model cell数、各passの時間・終了時memory、割当累計、GC/LOH/POH、command種別・face/metrics/font生成数、PDF font/image objectとFontFile stream数・bytesを保存した。終了時snapshotだけから段階内peakは断定しない。observerは内部AsyncLocalで、ConversionResultのセル診断を計測ログで増やさない。

## 前後・フェーズ別比較

時間：秒、RSS・割当：MiB。割当は累計でありpeakではない。

| 合成ケース・実装段階 | 時間中央値 | 最大peak RSS | 累積managed割当中央値 | PDF bytes | ページ |
|---|---:|---:|---:|---:|---:|
| 200文字セル：変更前 | 15.241 | 2296.3 | 45.9 | 69,436 | 3 |
| 200文字セル：フェーズ1 | 0.997 | 116.6 | 35.5 | 28,712 | 3 |
| 200文字セル：最終 | 1.360 | 111.5 | 30.1 | 28,712 | 3 |
| 20,000空セル：変更前 | 6.700 | 242.1 | 220.0 | 380,103 | 78 |
| 20,000空セル：フェーズ1 | 3.101 | 237.7 | 214.1 | 370,247 | 78 |
| 20,000空セル：最終 | 3.583 | 223.2 | 197.9 | 370,247 | 78 |

200文字セルで、変更前比の時間短縮は91.1%、RSS削減は95.1%。
小ケースでは起動・読み込みの比率が高く、フェーズ2はフェーズ1より時間が増えたrunもある。割当量は低下している。

200文字セルの内訳（ms、3回の中央値。readerはclosedXmlを含む。合計として重複加算しない）：

| 段階 | 変更前 | フェーズ1 | 最終 |
|---|---:|---:|---:|
| fonts.ms | 20.72 | 19.26 | 19.14 |
| closedXml.ms | 439.21 | 417.36 | 489.83 |
| reader.ms | 719.02 | 707.27 | 883.58 |
| diagnostics.ms | 7470.17 | 14.56 | 11.01 |
| TextMeasurePass.ms | 7230.39 | 66.57 | 70.02 |
| PaginationPass.ms | 58.24 | 42.31 | 68.55 |
| pdfPage.ms | 128.91 | 42.28 | 61.67 |
| pdfFinalSave.ms | 7.14 | 31.30 | 37.97 |

200文字セルの生成数：

| 項目 | 変更前 | 最終 |
|---|---:|---:|
| typeface生成（支持判定・行メトリクス・MDW） | 6401 | 1 |
| 行メトリクス計算 | 400 | 1 |
| resolved XFont生成 | 400 | 1 |
| PDF Save / Import | 4 / 3 | 1 / 0 |
| FontFile stream | 3 | 1 |
| FontFile圧縮stream bytes | 57,192 | 19,064 |

変更前の200セルでは、文字計測終了時にRSSが約2.3GiBまで増える一方、managed heapは約44MiBだった。メトリクス工程のnative face再生成とSkia cache保持を優先して直す根拠となった。ただし詳細なnative allocator traceは未取得で、RSS全量を単一処理へ帰属させる測定ではない。

変更前の文字あり20列×1,000行はSIGKILLとなり、cgroupのOOM killを確認。20列×100行も終了コード-9で、終了直前までにRSS 6194.0MiBを外部観測した。この失敗測定には別baselineプロセスとbuildの同時実行があり、OS上限到達時の単独プロセスpeak/完了時間を確定値として比較しない。3回中央値や完了時allocated/PDF bytesは取得不能。失敗後は完了可能な200セルと文字なし表で3回比較した。

## 大きさと構造を変えた最終実装

すべて3回測定。textNは20列×N行。

| ケース | 時間中央値（秒） | 最大peak RSS（MiB） | managed割当中央値（MiB） | PDF bytes | ページ |
|---|---:|---:|---:|---:|---:|
| text1000 | 6.857 | 270.0 | 300.0 | 478,727 | 78 |
| text5000 | 11.482 | 377.9 | 1381.2 | 2,300,783 | 387 |
| text10000 | 22.561 | 575.3 | 2697.7 | 4,578,269 | 771 |
| merges1000 | 4.173 | 268.6 | 300.7 | 492,979 | 84 |
| wrap1000 | 4.118 | 289.6 | 324.3 | 815,187 | 78 |
| far1000 | 4.183 | 275.9 | 325.6 | 478,727 | 78 |
| mixed1000 | 4.486 | 288.8 | 330.9 | 512,445 | 83 |

10,000行では累積割当が数GiBでもpeak RSSは約575MiB。
ページ数・セル数の増加に対し、5,000→10,000行の時間は約1.96倍。
画像・多重印刷範囲・タイトル・異なる用紙はmixed、結合はmerges、長文/結合文字/IVSはwrapで測定。range/trim、外字/emoji/fallbackは既存回帰も使用した。

## 同一プロセス10回

20,000文字セル。強制GCはハーネスのみ、変換終了後・計測時間外。GC後の指標：

| 回数 | managed heap（MiB） | RSS（MiB） | PrivateMemorySize64（MiB） |
|---|---:|---:|---:|
| 1 | 18.9 | 257.3 | 275.8 |
| 2 | 19.9 | 275.0 | 288.5 |
| 3 | 19.9 | 267.6 | 279.6 |
| 4 | 19.9 | 274.2 | 286.1 |
| 5 | 19.9 | 293.4 | 305.7 |
| 6 | 19.9 | 283.7 | 293.1 |
| 7 | 19.9 | 274.3 | 283.2 |
| 8 | 19.9 | 278.7 | 288.1 |
| 9 | 19.9 | 301.8 | 311.9 |
| 10 | 19.9 | 291.6 | 299.1 |

managed保持は初回後に安定し、RSSは上下して毎回の単調増加は見られなかった。allocator/CLRの予約領域とプロセス共通cacheの初回増加は残るため、「RSSが初回値へ必ず戻る」という保証ではない。多数の異なる外部フォントを追加する長期プロセスは未検証。

## 機能・package検証

- restore、指定のRebuild、全tests：警告0・エラー0、library 336件とCLI 30件成功。
- 200文字セル3ページ、20,000空セル78ページを変更前/最終で96dpi raster比較し、全81ページの画素差分0。許容差を広げず、PDF byte一致は条件にしていない。
- PdfOutputRegression、PdfContentProbe、GeometryRegression、FinalizedTextRendering、DrawingStateRegression、RangeTrimHyperlink、SkiaFontRegressionとPNG/SVG回帰を全件実行。新規テストはzero/hidden境界、疎な巨大範囲、書式共有、Register cache無効化・UTF-16 offsets、alias所有/例外/cancel解放、使用中fontの有効性、Save/Import回数、font共有、空ページ/用紙サイズ、ページ間cancel/sink abortを検証。
- libraryとCLIをpackし、netstandard2.1 DLL/README/notices、CLIのnet10.0構成/font DLLを確認。package内ExcelRenderer.dllが最終Release DLLと一致することをhash比較。

このLinux imageの既定Fontconfigは、Skiaで読めないOpenAI WOFF2をsans-serifへ選択していた。テストでは次の設定を使用した（製品コードのfont既定値は変更しない）：

```bash
cat > /tmp/excel-fonts.conf <<'EOF'
<?xml version="1.0"?>
<!DOCTYPE fontconfig SYSTEM "urn:fontconfig:fonts.dtd">
<fontconfig>
  <include>/etc/fonts/fonts.conf</include>
  <dir>/workspace/ExcelRenderer/third_party/NotoSansJP</dir>
  <cachedir>/tmp/excel-font-cache</cachedir>
  <selectfont><rejectfont><glob>/usr/share/fonts/openai-sans/*</glob></rejectfont></selectfont>
</fontconfig>
EOF
mkdir -p /tmp/excel-font-cache
dotnet restore ExcelRenderer.slnx
dotnet build ExcelRenderer.slnx -t:Rebuild --no-restore --verbosity:minimal
FONTCONFIG_FILE=/tmp/excel-fonts.conf dotnet test ExcelRenderer.slnx
```

## 再現と未実施項目

[ハーネスの手順](../tools/ExcelRenderer.Performance/README.md)で生成・新規プロセス3回・baseline export・同一プロセス10回を再実行できる。Fontconfigのdirはcheckout位置に合わせる。入力SHAはZIP timestamps等で再生成時に変わり得るため、同じ生成済みXLSXを前後で共有する。

全ページのRenderCell/command、sheetモデル、最終PdfDocumentのリソースは保持する。完全なpage単位生成・描画、選択範囲のみのmodel生成、OSフォントの遅延byte読込、同じ画像decode/XImageの再利用、SAX readerは未実施。現在の合成ケースでは主要なnative再生成問題を解消でき、大表ではreader/model処理と線形のcommand/PDF保持が残るため、これらの大きな変更は実帳票のphase計測に基づく次段階とする。styled blankのAll→Contents置換、既定のOSフォント無効化、罫線dedup/連結、PDF raster化、プロセスglobalのResolvedFontData削除は行っていない。

実帳票、Windowsの大量OSフォント、全行列書式ケースの大規模実測、dotnet-traceによるnative allocator帰属、異なる外部fontを増やす長期運用は未検証。性能目標50%は合成200セルで達成したが、実帳票の削減率は未確定。
