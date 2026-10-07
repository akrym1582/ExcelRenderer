# PNG・SVGの省メモリ・高速化

## 適用範囲

PDFの性能改善で入れた書式共有、疎な座標・結合セルの索引、文字計測キャッシュ、
変換中のnativeフォント共有は、`ExcelConverter.RenderAsync`のPNG・SVGにも既に適用されていた。
PDF文書の直接組み立てとPDFフォントの共有はPDF固有の処理である。

今回追加した改善は次の3点。

- `PngRenderer`・`SvgRenderer`の直接呼び出しでも、描画呼び出し内でnativeフォントを共有する。
  複数ページの`Render`ではページ間でも再利用し、成功・例外のいずれでも終了時に解放する。
  既存の変換スコープがあればそれを使い、そのスコープはレンダラーが解放しない。
- PNGでは`SKImage.FromBitmap`を経由せず、既存のbitmapを直接エンコードする。
  immutable画像作成のための全画素コピーを省く。ページ描画・連続キャンバスともに適用する。
- SVGは寸法・viewBoxを書き換える際に`XDocument`で全XMLツリーを作らず、
  `XmlReader`・`XmlWriter`で転送する。名前空間・描画要素・画像のdata URIを保持する。

## 計測

Linux、.NET SDK 10.0.401、Releaseビルド。変更前は`64c2610`。
同一の計測コードを変更前後で使い、各ケースは別プロセスで起動し、その中で3回描画する。
時間とmanaged割当は3回の中央値、RSSはプロセスのlifetime peakの最大値。
RSSには準備処理も含む。managed割当は累計でありpeakではない。
ビルドと重なった図形ケースはビルド終了後に前後とも測り直した。

入力は描画命令で、XLSXの読み取り・レイアウトは含まない。
文字は明示登録した同梱Noto Sans JP Regularで、system/font packは無効にした。
解決処理は計測前にwarm upし、描画時のnativeフォント取得・解放は計測内に含める。

- 文字：1ページ20個の同じ「帳票 ABC 123」、3ページ、1200×1200、72 DPI。
- 図形：1ページ10,000個の矩形、3ページ、1200×1200、72 DPI。
- 大きなPNG：4096×4096画素、3ページ、72 DPI。

| ケース | 時間中央値 前→後（ms） | managed割当 前→後（MiB） | lifetime peak RSS 前→後（MiB） |
|---|---:|---:|---:|
| PNG・文字 | 8529.61 → 289.42 | 0.449 → 0.069 | 4000.82 → 63.45 |
| SVG・文字 | 817.87 → 31.89 | 3.355 → 2.949 | 4002.28 → 65.18 |
| PNG・図形 | 207.51 → 225.22 | 3.273 → 3.285 | 76.24 → 71.46 |
| SVG・図形 | 259.20 → 245.68 | 26.293 → 14.179 | 99.70 → 90.12 |
| 大きなPNG | 1859.23 → 1839.09 | 0.005 → 0.031 | 179.52 → 116.76 |

直接呼び出しの文字ケースでは、変更前は呼び出すたびにnativeフォントを作り直し、
Skiaのstrike cacheにフォントデータが残っていた。3回目までRSSが増加する一方、
変更後は各描画呼び出しで物理faceの生成数が1になり、終了時に未使用strikeを解放する。
この比較は、既に共有済みの`RenderAsync`が同じ倍率で速くなるという意味ではない。

SVG図形ケースのmanaged割当は約46%減った。大きなPNGではRSSが約63MiB減り、
4096×4096×4 bytesの画素コピーを省いた効果と整合する。
PNG図形ケースの時間は約9%増えたため、全ケースの高速化を主張しない。
小さな時間差には共有VMの変動とストリーム出力方式の差が含まれる。
実帳票、Windows、異なるフォントを多数使う長期運用は未計測。

[raw JSONLと集計](../tools/ExcelRenderer.Performance/results/image-summary.json)には
`image-before-*.jsonl`・`image-after-*.jsonl`として3回分の値と出力SHA256を保存した。

## 出力・回帰検証

5ケース×3回×3ページの45成果物を前後比較した。
PNGは全27枚がbyte単位で一致した。SVG全18枚はXMLの要素名・属性・子の順序・
非空白の内容が一致した。XMLの整形空白と属性順は比較対象外で、SVGのbyte一致は要求しない。
画像埋め込み、文字パス、罫線、crop/rotation、viewBox、非seek出力、
描画状態復元などは既存PNG・SVGの回帰テストでも検証する。

直接呼び出しの全入口でのface共有・解放、例外時の解放、既存スコープの所有権を
10件のテストで検証した。Rebuildは警告0・エラー0。
library 354件とCLI 30件のテストを実行した。
テスト環境のFontconfigは、既存PDF計測と同様にSkiaで読み込めるTTFを使う設定にした。

## 再現

リポジトリルートから実行する。形式は`png`・`svg`、ケースは`text`・`shapes`・`large`。
`large`はPNGの比較用。

```bash
dotnet build tools/ExcelRenderer.Performance/ExcelRenderer.Performance.csproj -c Release
dotnet tools/ExcelRenderer.Performance/bin/Release/net10.0/ExcelRenderer.Performance.dll direct-images /tmp/png-text png text 3
dotnet tools/ExcelRenderer.Performance/bin/Release/net10.0/ExcelRenderer.Performance.dll direct-images /tmp/svg-shapes svg shapes 3
dotnet tools/ExcelRenderer.Performance/bin/Release/net10.0/ExcelRenderer.Performance.dll direct-images /tmp/png-large png large 3
```

変更前のcheckoutにも`Program.cs`の`direct-images`分岐と`ImageRenderingBenchmark.cs`を
コピーして、同じRelease設定でビルドする。

PNGのbitmap本体、SVGのbyteバッファ、シートモデル、全ページの命令は保持する。
全工程を一定メモリで処理する変更ではない。PDFsharpによる共通文字計測のためのロックも保持する。
画像decodeの再利用、ページ命令の逐次生成、SVGの中間byteバッファの除去は今回の対象外。
