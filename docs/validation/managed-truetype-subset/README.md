# Managed TrueType subset validation

実装と Linux での検証を完了しました。通常 TrueType の subset と TTC/OTC face 抽出は
Core の BCL-only C# 実装です。カラー描画は既存 Skia 経路を維持し、独自 native adapter の
ソース・P/Invoke・ビルド・CI 前処理・配布資産・専用ライセンスを撤去しました。

- 開始 main SHA: `9157b80788d590694ef90d04dbaea9872773c6d6`（origin から取得）。
- 実装終了 SHA: `4731249a8ca00f7f73b8afc1c01844ecf801c59d`。
- ブランチ: `feature/managed-truetype-subset`。後続コミットはガイド・検証スクリプト・結果記録です。
- 環境: Debian 13 / Linux x64、SDK 10.0.401、runtime 10.0.12。実行日 2026-10-18〜19 UTC。
- 対応: 単体固定 TrueType、TTC v1/v2 内の選択 TrueType face、既存 COLR v0/v1・CBDT カラー経路。
- PDF 非対応: CFF/CFF2・OTTO・可変・WOFF/WOFF2・旧 true/typ1。PNG/SVG は PDF 専用拒否を継承しません。

API・CLI と内部契約は[英語ガイド](../../managed-truetype-subset.md)・
[日本語ガイド](../../managed-truetype-subset.ja.md)を参照してください。

## クリーン build / test / pack

adapter を一度もビルドしていない新しい worktree で restore、Release rebuild、全テストを実施しました。
/tmp の別ボリュームが pack 中に満杯になったため、worktree を
`/workspace/excelrenderer-validation/clean` へ移し、restore・警告ゼロの rebuild・pack・publish・
配布監査を再実行しました。初回の容量不足を成功結果へ含めていません。

| 検証 | 結果 |
| --- | --- |
| locked restore / Release rebuild | 成功、警告 0 / エラー 0 |
| Core | 167 passed |
| Main | 686 passed |
| Tool | 105 passed |
| Mapping | 172 passed |
| OpenXml | 206 passed |
| Formulas | 833 passed |
| 合計 | **2,169 passed、失敗・skip 0** |
| Skia native を除いた使い捨てコピーでの純 C# subset/TTC テスト | 56 passed |
| pack / local publish | 4 公開パッケージのみ、Core/OpenXml/Formulas DLL/XML の共有 hash 一致 |
| 独自 adapter・targets・専用ライセンスの配布混入 | なし |
| 削除済み PDF エンジンの依存監査 | 22 projects / 788 AssemblyRef inventories / 4 packages で検出なし |
| Core 依存方向 / namespace / フォント fixture hash | 成功 |
| 隔離 NuGet consumer と installed CLI | 10 consumer 経路成功 |

package-only consumer では日本語・IVS・カラー混在 PDF、Core/Main の TTC face 1 を出力し、
Poppler で元の Unicode（異体字セレクター・絵文字を含む）を抽出できることを確認しました。
installed CLI の TTC 出力では実際の Bold face 名も検査し、CFF/可変の使用時は理由付き拒否・出力未commit を確認しました。
`FontCollectionTests` は style 別 index、同一 path の cache 分離、file/memory/custom resolver、
凍結 snapshot、除外ページの非対応 font、PNG/SVG の既存成功も検証しています。

証拠: [results.json](results.json)、[build.log](build.log)、[tests.log](tests.log)、
[pack.log](pack.log)、[packages.json](packages.json)、[consumers.log](consumers.log)。

## 独立検証

fontTools 4.61.1 で 5 個の出力 sfnt を読み、独立した checksum 計算で全体・各 table・
head 調整・alignment を確認しました。Skia の同一 GID のパス・advance 比較も成功しました。

MuPDF による fullEmbed/subset 6 組の比較では、254 ページすべての text・word position・box が一致。
144dpi は全ページ、300dpi は小規模全ページと 50/200 ページの先頭・中央・末尾を比較し、
計 264 回の raster 比較で差分ゼロでした。開始 commit の旧方式と同じ日本語・IVS・カラー絵文字を
描いた 4 組も 144/300dpi の 8 比較で差分ゼロです。閾値緩和・画像合わせ・マスクを使っていません。

COLR v0/v1 と TTC 内の対応カラー face はそれぞれ 504 個の色付き pixel を独立 reader で確認しました。
日本語・IVS・Noto Color Emoji 混在では 11,850 個です。カラー face は輪郭 subset 呼出し 0。
旧方式とのカラー比較は image stream hash と Type3 font 数も一致しています。

証拠: [independent-validation.json](independent-validation.json)、
[color-streams.json](color-streams.json)、[font-evidence.json](font-evidence.json)。

## 容量と形式上の差異

IPAmj の少数字 subset は **46,676,744 → 1,440,340 bytes（3.09%）** で、既存の 1/10 未満条件を維持。
Noto Sans JP は 5,472,784 → 330,976 bytes、欧文 Mono は 512,836 → 94,048 bytes です。
使用 GID 0/26795/26796/23477 の元の輪郭を維持しています。

旧 HarfBuzz と比べると、今回の方式は cmap・post・metrics と元の GID 領域をそのまま保持します。
IPAmj の主な差は post 32 → 671,708、cmap 52 → 270,124、loca 53,596 → 245,444 bytes。
Noto の cmap は 228 → 122,858、loca は 31,400 → 68,416 bytes です。
これが小 PDF の増分の主因です。混在カラー PDF の増分も通常輪郭 font stream に由来し、
色付き画像の bytes/hash は同一でした。通常 TrueType の失敗を全埋込へ退避していません。
[table-comparison.json](table-comparison.json) に table ごとの差を保存しています。

計画の「入力 loca は偶数境界」という条件は調整しました。同梱 Noto Sans JP と IPAmj は
long loca に奇数 offset を含むため、これを拒否すると必須の既存フォントを使えません。
long 入力は単調性・範囲を検査して受け入れ、出力をゼロ padding で偶数境界へ揃えます。
short 入力は元々2byte単位です。時刻・GID・Unicode・メトリクスは変更していません。

## 性能

最終の測定値は [performance-summary.json](performance-summary.json)、全サンプルは
[performance-raw.json](performance-raw.json)、入力/font/assembly hash と条件は
[performance-conditions.json](performance-conditions.json) に保存しています。
Release、同一 SDK/OS/入力/font bytes、各 worker 3 warmups + 1 測定を5回、旧/新を交互に実行しました。
旧方式は開始 commit の隔離 worktree にだけ公式固定版 adapter をビルドしています。

| 帳票 | 旧 ms | 新 ms | 時間比 | peak RSS 比 | 新 PDF bytes |
| --- | ---: | ---: | ---: | ---: | ---: |
| invoice | 57.06 | 60.82 | 1.066 | 0.992 | 136,405 |
| japanese | 470.55 | 498.54 | 1.059 | 1.121 | 742,092 |
| small | 23.71 | 31.36 | 1.323 | 1.065 | 114,046 |
| ivs | 235.93 | 257.98 | 1.093 | 1.083 | 657,280 |
| color | 275.51 | 303.25 | 1.101 | 0.970 | 670,597 |
| 50-pages | 845.18 | 841.16 | 0.995 | 0.981 | 233,001 |
| 200-pages | 1281.35 | 1324.10 | 1.033 | 0.981 | 591,526 |

全7ケースで時間・peak RSS の中央値は旧版の2倍以内でした。請求書・日本語帳票は既存の1MiB未満を維持しています。

TTC の通常/太字 2 face、3 サイズを含む描画では、face 抽出・subset 作成は各 face 1 回でした。
TTC metadata/抽出時間・allocation の 5 サンプルは font-evidence.json に記録しています。
旧 API は非ゼロ index 選択がなかったため TTC の旧版との相対性能は未測定です。
peak RSS は fresh worker process の高水位です。文書を越えた保持の確認用に、反復後の GC 済み
managed bytes / working set の全サンプルも保存しています。追加の200ページ×12変換では managed bytes は新方式 37,390,672、旧方式 約30,606,792 bytes へ収束しました。
追跡対象の typeface/subset/SKFont 作成・Dispose 数も全変換で一致しています。
[retention-12.json](retention-12.json) に記録しています。

## 未実施事項と再実行

Windows はこのセッションでは実行していません。Windows x64 / Linux x64 の build・PDF/CLI・
pack・consumer matrix を [.github/workflows/pdf-build-distribution.yml](../../../.github/workflows/pdf-build-distribution.yml)
に維持しました。Linux CI には fontTools / MuPDF / Poppler の独立検証も追加しています。
sbix 等の専用実フォント golden は今回同梱されておらず、全 bitmap/SVG 方式・新しい絵文字 sequence の
対応を保証していません。既存 sbix 判定・Skia 描画分岐は維持しています。

```bash
dotnet restore ExcelRenderer.slnx --locked-mode
dotnet build ExcelRenderer.slnx -t:Rebuild -c Release --no-restore --verbosity:minimal -m:1
source scripts/test-fonts.sh
dotnet test ExcelRenderer.slnx -c Release --no-build --no-restore -m:1
dotnet pack ExcelRenderer.slnx -c Release --no-build --no-restore -m:1 -o artifacts/packages
python3 scripts/validate-formulas-packages.py --packages artifacts/packages --work /tmp/isolated-consumers
dotnet tools/ExcelRenderer.PdfComparison/bin/Release/net10.0/ExcelRenderer.PdfComparison.dll --managed-fonts artifacts/managed-fonts
python3 scripts/validate-managed-fonts.py --evidence artifacts/managed-fonts
python3 scripts/benchmark-managed-fonts.py --baseline-assembly /path/to/isolated-old/ExcelRenderer.Performance.dll --output artifacts/managed-font-benchmark
```

比較用 artifact は通常製品へ含めず、baseline PDF とフォントの既存 hash は変更していません。
