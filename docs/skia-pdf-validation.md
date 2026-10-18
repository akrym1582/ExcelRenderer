# Skia PDF実装・検証記録

> Historical validation for the commit recorded below, using the former native subset adapter.
> Current builds use [managed TrueType subsetting](managed-truetype-subset.md);
> the old adapter build steps and results below do not establish current validation.

## 対象と状態

`feature/skia-pdf-backend`。調査対象と分岐元のorigin/mainは
`8d23c0725f60126f5e38c9a9e9a612a6d276b93a`。実装・自動検証・配布検証の結果を本書と
[機械可読結果](skia-pdf-results/)に記録する。[ドラフトPR #89](https://github.com/akrym1582/ExcelRenderer/pull/89)。実装SHAは`d843d02d55343355b27f6cefb7b0decd4e73ae03`。
既定はPDFsharp。今回の目的は描画切替であり、PDFsharp依存・計測器・font gateは保持する。

Linux x64 / Debian 13 / SDK 10.0.401 / runtime 10.0.12。
PDFsharp 6.2.3、SkiaSharp/同Linux native assets 4.151.0は変更していない。
[environment.json](skia-pdf-results/environment.json)に依存版・font/native hashを記録。
変更前のRebuildは警告0・エラー0、1,944テスト成功・skip 0。

## API・CLI

```csharp
await ExcelConverter.ConvertToPdfAsync(inputPath, outputPath,
    new PdfExportOptions { PdfBackend = PdfBackend.SkiaSharp });

await ExcelConverter.RenderAsync(inputStream,
    new RenderRequest { OutputFormat = OutputFormat.Pdf, PdfBackend = PdfBackend.SkiaSharp },
    new SingleStreamOutputSink(outputStream));
```

```bash
excelrenderer pdf report.xlsx -o report.pdf --pdf-backend skiasharp
excelrenderer render report.xlsx --format pdf -o report.pdf --pdf-backend skiasharp
```

`--data`と既存formula/reader設定にも伝播する。CLI名は小文字。
不正enumは入力・sink open前、不正CLI値/非PDFへの明示指定はパース時に拒否。
APIの非PDF要求は既定PdfSharp値だけを互換性のため許容する。
公開PdfSharpRendererとCore単独APIは従来どおりPDFsharp専用。

## 実装の境界

- main内部の`IPdfDocumentWriter`で文書・ページ・確定・中断を分離。
  PDFsharp writerは既存rendererとCore Saveを利用する。
- レイアウト、選択、preflight、診断、リンク解決、sink commitは共通。
  `PdfCommandFinalizer`が未確定の互換文字・header/footer・図形文字を共通のlayout serviceで
  確定する。旧measurerのrunなしlayoutも幅・高さ・baselineを保ってrunだけを補完する。
  Skia writer内で再計測・折返しを行わない。
- Skiaは同じpage planから1ページずつ再生成してglyph集合を収集し、順次再生成して描画。
  全ページcommands、bitmap、SKPictureを保持しない。比較では両backend間と収集/描画間の
  確定command fingerprintを一致確認する。内部比較用の固定時計で日時も一致させる。
- 元SourceText・UTF-8・grapheme cluster・元glyph IDをtext blobに保持。
  IVS、補助平面、康熙部首、結合文字、連続空白を勝手に正規化しない。
  普通のglyph位置には元sfntのhmtx/head/hheaを使い、Skia advanceの量子化を回避する。
- 実バイトSHA-256単位で文書内typefaceを共有し、サイズ別SKFontも文書内で再利用。
  原書体で計測・字形選択し、縮小書体は描画専用。等価な別ResolvedFont/byte[]も共有する。
  原書体・縮小書体・font/blob/native文書を解放し、未使用strike cacheのpurgeは変換末尾にまとめる。
- 呼出側のmeasurer/streamを破棄しない。各ページ・再帰走査・font単位subsetの前後・最終writeで
  cancellationを確認する。同期native呼出の途中を瞬時に割り込む保証はない。
  未収集glyph/faceが第2パスに現れたら失敗し、欠字PDFを確定しない。
- 画像decode診断の構築はCoreの既存helperへ集約。Skiaでもseverity/位置/strict時のabortを保持。
  Coreの依存方向・公開スコープ・manifest schemaは変更しない。

### 修正した互換性問題

1. Skiaが用紙の小数ptを整数に丸めるため、内部deviceを外接整数寸法で確保し、差分だけ原点移動して
   正確な寸法でclipする。後処理で元MediaBox/CropBoxへ戻す。内容の縮放はしない。
   小数pt用紙の右端の細線は144/300dpiで画素一致。
2. スタイル/文字の原点とadvanceを元レイアウト・元フォントへ統一。
   図形文字も両backendへ渡す前に確定し、角丸の半径をPDFsharpと一致させた。
3. Skia PDFはsampling指定だけでは`/Interpolate`を出さず、拡大画像が粗くなっていた。
   非mask画像の補間メタデータを既存XImageと合わせ、画像byteは変更しない。
4. HarfBuzzの既定subsetは`.notdef`輪郭を削除する。`RETAIN_GIDS | NOTDEF_OUTLINE`を指定し、
   欠字枠の消失を修正。元glyph 0の輪郭一致と欠字fixtureの画像一致も検証した。
5. nonseekable sinkで保存位置が必要な場合にCountingStreamの実書込位置を返す。
   両backendの生成PDFを別parserで読み直し、失敗/cancel時はabort一回・成果物なしを確認。

### 限定的なPDF構造後処理

`SkiaPdfCompatibilityFinalizer`は、正確なpage boxes、制御文字除去・512文字上限のリンクtooltip、
非mask画像の`/Interpolate`のみ補う。Skiaのnative URL/内部destination annotationを使用し、
前方/後方参照を登録する。PDFsharpで文字を再描画・再計測・フォント再埋込しない。
後処理前後の抽出文字列とfont/image decoded stream hashを一致確認する。

中間spoolは`RenderRequest.Buffering`を使う（既定8MiB、超過時temp退避）。
成功・中断の両方で退避ファイルが削除されるテストを追加。
この後処理が残るため、**Skia単独化・ライセンス移行の完了ではない**。
将来native APIで同じメタデータを出せる段階で後処理を置き換える。

## Native配布・font形式

```bash
python3 scripts/build-harfbuzz-subset.py
```

`native/harfbuzz`の薄いC ABI adapterが公式HarfBuzz 10.2.0を静的リンクする。
公式archiveのSHA-256を固定し、改変された展開済cacheも拒否する。
通常のsystem libharfbuzzやSkia内蔵HarfBuzzへ依存しない。
CMake/C++ compiler、`--archive`/`--cmake`による再現手順を用意。
生成先`native/runtimes/<rid>/build.json`は取得元・版・source/binary hash・ライセンス・exportsを記録する。
Old MITの完全なCOPYINGと通知をmain/Toolへ同梱。Python等は生成時runtime依存ではない。

main nupkgはRID別`runtimes/<rid>/native`、Toolは実行ディレクトリへadapterを同梱する。
ProjectReference/publishのhost assetもコピーし、pack時にhost nativeがなければ失敗する。
CIでWindows x64/Linux x64を個別build・実行・pack・隔離consumer検証し、releaseで両assetを集約する。
ローカル一OS分nupkgを全OS対応リリース成果物とは扱わない。他RIDは未検証である。

[font-formats.json](skia-pdf-results/font-formats.json)とoriginal MITの小型font fixtureで、
TrueType/CFF/可変TrueTypeの既定軸/TTCのface 0/COLR v0/v1を確認。
C#でsubset/描画し、非color glyphの元輪郭を保持、Poppler/MuPDFで`A B`を抽出、COLRの赤い画素も確認。
CBDTは固定Noto Color Emojiの実帳票/Unicode fixtureで検証する。
公開APIにはcollection index/variation座標指定はなく、明示登録したTTCは現行共通PDFsharp計測で
両backendとも失敗する既存制約を同じテストで記録した。Skiaだけが既存のfont互換性を狭めたものではない。

## 自動検証

```bash
source scripts/test-fonts.sh
dotnet restore ExcelRenderer.slnx --locked-mode
dotnet build ExcelRenderer.slnx -t:Rebuild -c Release --no-restore --verbosity:minimal -m:1 -p:UseSharedCompilation=false
dotnet test ExcelRenderer.slnx -c Release --no-build --no-restore -m:1
python3 scripts/check-source-namespaces.py
python3 scripts/check-core-dependencies.py
dotnet pack ExcelRenderer.slnx -c Release --no-build --no-restore -m:1 -p:BuildProjectReferences=false -o artifacts/skia-reviewed-packages
python3 scripts/package_distribution.py --packages artifacts/skia-reviewed-packages --output artifacts/skia-reviewed-package-inventory.json
python3 scripts/validate-formulas-packages.py --packages artifacts/skia-reviewed-packages --work /tmp/excelrenderer-skia-final-reviewed-consumers
```

Rebuildは警告0・エラー0。最終テスト集計は[verification.json](skia-pdf-results/verification.json)。
全2,008テスト成功・失敗0・skip 0（Core 85、Formulas 833、Mapping 172、OpenXml 206、Main 614、Tool 98）。
既存PNG/SVG/RichTextの回帰も含む。
CLIテストは実行中のテストと同じRelease/Debug構成のToolを選び、古いDebug出力を誤って検証しない。
共通の文字確定処理を加えたPDFsharpのheader/footer・図形の保持XLSXは、変更前との
[ページ数・文字列・144dpi全画素一致](skia-pdf-results/pdfsharp-legacy-compatibility.json)も確認。
APIファイル変換のcached/recalculate値、両CLI/Mapping/reader設定、borrowed measurer、nonseekable I/O、
preflight/collect/subset/draw/page-complete/write時のcancel、write失敗、strict診断、再実行、異なるfontの同時4変換を確認。

4 nupkg、公開allowlist、共有DLL/XML hash、非公開依存なしを確認。
空のNuGet cacheを使う9つの隔離consumerで両backendの実PDFを生成。
Fontsなしのユーザーfont利用、インストール済みToolのpdf/render、adapter欠落時の具体的失敗・出力abort、
adapterなしでも既定PDFsharpが動くことまで確認した。

この環境の並列MSBuildはnamed pipeの権限エラーが出るため`-m:1`を使用。
以前の重複build・古い一時cacheによる失敗は正式結果から分離し、逐次Rebuild/テストを実行した。

## 比較結果と許容差分

```bash
python3 scripts/compare-pdf-backends.py --no-build --output artifacts/pdf-comparison-complete
python3 scripts/check-pdf-comparison-mutations.py --baseline artifacts/pdf-comparison-complete
# 例外適用前の固定screening閾値の超過も再現できる（終了値1）:
python3 scripts/compare-pdf-backends.py --no-generate --no-exceptions --output artifacts/pdf-comparison-complete
```

[比較ハーネス](../tools/ExcelRenderer.PdfComparison/README.md)は同一commandsと公開API E2Eの二経路。
25 fixtureでA4/A3/Letter/小数pt/横向き/混在用紙、空ページ、印刷範囲/改ページ、複数sheet、
range/page選択/trim、URL/内部リンク/除外宛先/危険URL/Hyperlinks.None、header/footer/固定日時、
全border style、細線/double/ARGB、rich text/折返し/shrink/配置/回転/縦書き、
PNG/JPEG/透過/反復/crop/flip/rotate/clip、全図形種類/図形文字/重なり/viewport原点を検証する。
50/200ページは性能・font object共有検証で補う。取消線等の既存非対応書式は既存の診断を保持する。

MediaBox/CropBoxは0.01pt、リンク矩形/宛先は0.1pt以内。Unicodeの独立期待値をPoppler/MuPDFで確認。
元fontの使用glyphと複合glyph輪郭、full-font版からの90%以上削減と144dpi画素/両抽出器の一致、
後処理のresource保持を確認。[report.json](skia-pdf-results/report.json)に記録する。

画像は144/300dpiでリサイズ・自動位置合わせ・端の切捨てをしない。
RGB差>16の非背景union比率1%、相手inkから1px超の非一致比率0.1%を**変更せず**適用する。
そのscreening超過51領域を次の根拠で個別レビューした。

| 範囲 | 件数 | 判定と再検証する構造根拠 |
| --- | ---: | --- |
| IVS・色絵文字（4用紙と2ページlinks） | 24 | 元glyph輪郭と参照pathの全control pointが0.0001pt以内。CBDT glyph ID・Type3 charproc・参照画像baselineを照合。path/font AAと64px-em/既存4x画像の補間差。 |
| 請求書・日本語帳票の小文字等 | 24 | 両fontの元輪郭、glyph ID/位置/色/size、非glyph vector paintを照合。PDFsharpの3桁Tf量子化により最大size差0.000461pt、累積原点差0.017701pt。 |
| Ellipse/300dpi | 1 | 同じ中心・半径・fill/stroke。4 cubic対32 cubicの円弧近似。独立楕円方程式の最大正規化残差0.000546/0.000025、1px超の非一致0。 |
| image-2/crop | 2 | 既存PDFsharpのcrop無反映の改善。元32×32画像とcrop座標から計算した6点のRGBにSkiaは最大1/255差で一致、PDFsharpは最大32/255差。 |

[個別差分](skia-pdf-results/differences.json)と
[レビュー済み領域](../tools/ExcelRenderer.PdfComparison/reviewed-differences.json)を保持。
例外はfixture/page/region/dpiと確定command hashで限定し、個別の実測上限も固定する。
構造/元輪郭が不一致、hashが変化、個別上限超過なら失敗させる。ページ全体マスク・golden差替えはしない。
未分類/要修正は0。IVS削除・余分な線の注入は同じ例外下でもそれぞれ9/5ゲートで検出した。
HTML、PDF、全差分/overlay画像は`artifacts/pdf-comparison-complete/`、CIではtest-output artifactに保存する。

容量は請求書約35KB・日本語帳票約105KBで1MiB未満。full-font版比98.90%/99.69%以上削減。
[50/200ページの共有確認](skia-pdf-results/multipage-font-sharing.json)では同一書体のfont objectが各1個。
50/200ページの埋込font streamは各169,416 bytes、PDF全体は123,021 / 474,622 bytes。
画像多数の実PDFは6,192,058 bytes（12ページ）、font stream 169,416 bytes、画像/mask decoded stream
7,340,032 bytes・48 objectで、画像に1MiB制限を適用していない。
後処理のためにページ全体を画像化・通常文字をoutline化することはない。

## 性能・リソース

```bash
python3 scripts/benchmark-pdf-backends.py --no-build --configuration Release --warmups 3 --output artifacts/pdf-benchmark-final-reviewed
```

小帳票/grid/画像32種/50/200ページを並列process 1/4、各5 roundのfresh processで実行し、各processの
3回warm-up後の1回を測定する（250測定、warm-up込み1,000変換）。中央値/最大値・subset/生成/保存時間・
出力bytes・peak working set・allocationをraw JSONへ保存する。画像は32種類の256×256 PNG、半数にalpha。
ページ数50/200も実生成結果で検証する。

正式なRelease結果は[benchmark-summary.json](skia-pdf-results/benchmark-summary.json)、
全250測定は[benchmark-samples.csv](skia-pdf-results/benchmark-samples.csv)、条件・hashは
[benchmark-environment.json](skia-pdf-results/benchmark-environment.json)に記録。

| fixture | 並列 | 時間比 | peak WS比 | 判定 |
| --- | ---: | ---: | ---: | --- |
| invoice | 1 | 1.017 | 1.201 | 成功 |
| invoice | 4 | 1.109 | 1.151 | 成功 |
| grid | 1 | 1.825 | 1.171 | 成功 |
| grid | 4 | 1.216 | 1.248 | 成功 |
| images | 1 | 0.622 | 1.077 | 成功 |
| images | 4 | 0.606 | 1.089 | 成功 |
| 50-pages | 1 | 0.741 | 1.088 | 成功 |
| 50-pages | 4 | 0.980 | 1.065 | 成功 |
| 200-pages | 1 | 1.374 | 1.098 | 成功 |
| 200-pages | 4 | 1.190 | 1.072 | 成功 |

全10条件で時間・メモリとも中央値比2倍以内。最大値と段階別時間もJSON/CSVに保存し、一般的な速度優位は主張しない。
以前のDebug/1 warm-upの請求書時間比2.083は
[初期記録](skia-pdf-results/benchmark-initial-debug.json)として保持し、Releaseとの差をそのまま実装修正の改善率とは主張しない。
SKFont再利用と重複purgeの整理を行い、起動/JIT/GCの分散は両engine同条件のwarm-upで分離する。
比較CPU負荷が重なった途中測定は中断し、正式な全matrixは他のbuild/比較/consumerを走らせず実行する。
結果の選別や2倍基準の緩和はしない。`OutputBytes`はdiscard sinkのwrite counterなので実PDF容量は比較ハーネスで判定する。

同一processで各engineを20回変換し、その都度GC後の状態も
[retention.json](skia-pdf-results/retention.json)へ記録した。Skiaの最後5回のmanaged使用量差は264 bytes、
working set差は344,064 bytesで、計測用native faceの生成/破棄数も全回一致した。
working setにはJIT・allocator/cacheの常駐分が残るため、解放後にRSSがゼロへ戻るとは説明しない。

## Windowsと手動viewer確認

実装SHA `d843d02`の[CI](https://github.com/akrym1582/ExcelRenderer/actions/runs/39103573971)は全7 job成功。
Windows x64 / Ubuntu 22.04 x64ともRebuild警告0、native/API 48件、CLI 98件、4 package、9隔離consumerが成功。
通常のLinux全suite・独立比較/変異・Core/Formulasの両OS検証も成功した。
[CI結果](skia-pdf-results/ci.json)、[Linux native](skia-pdf-results/linux-ci-native.json)、
[Windows native](skia-pdf-results/windows-ci-native.json)にjob・source/binary hash・exportsを保持。
生成物は同runの`release-pdf-native-Linux` / `release-pdf-native-Windows` artifactを参照。
このLinux環境でEdge/Adobe ReaderのGUI操作はできないため、**手動確認未実施**。
計画書7.4に従い、自動抽出確認と混同しない。手順は以下。

1. CI artifactの`commands-1/skiasharp.pdf`をEdge/Adobe Readerで開く。
2. `日本語`、`𠮷野家`を検索。IVS行をコピーし、辻+E0102/辻+E0103/葛+E0102が保持されることをUnicode scalarで確認。
3. `links`で前方/後方リンク、URL、tooltipを確認。
4. 100%の印刷プレビューでA4/Letter/小数pt用紙と紙端細線、画像crop/透過、図形の切れを確認。

## 次段階（今回の対象外）

Skia計測器、Core単独のbackend選択、PDFsharp gate/resolver除去、限定後処理の置換、
最後にPDFsharp参照撤去を別段階で検討する。これらの完了や未検証RID対応を主張しない。
