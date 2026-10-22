# LibreOffice / 本体 / Core 比較検証

**Coreページ増加の修正後検証は[追補](core-pagination-fix/README.md)を参照してください。** この親ディレクトリの集計・画像・判定は修正前の履歴として保持しています。追補では罫線の追加数値監査と、ユーザー指示に基づくCore richtext未対応の契約明確化も記録しています。

検証対象コミット: `ba5f8350189bb92a4b91e31e5aa632721618e974`（開始時の最新 `origin/main`）。
参照は LibreOfficeDev 26.8 の暫定PDFで、Windows Excelの正解PDFではありません。
**全22入力を両版で実行し44PDFを生成、3組×22件の比較を実施しました。生成成功は描画一致を意味しません。**
製品コード・元XLSX・参照PDF・参照manifestは変更していません。

| 実行エンジン | 入力数 | 生成PDF | ページ | 失敗/未実行 | 契約上の機能差が観測されたケース | 未解決の部分書式契約 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 本体（cached、CF有効） | 22 | 22 | 31 | 0 | 0 | 0 |
| Core（保存値、Noto Sans JP通常体1個） | 22 | 22 | 34 | 0 | 10 | 2 |
| LibreOffice参照 | 22 | 22（既存） | 32 | — | — | — |

| 比較組（左 / 右） | 比較記録 | ページ数一致 | 文書全体の非空白文字インベントリ一致 |
| --- | ---: | ---: | ---: |
| LibreOffice / 本体 | 22 | 21 | 14 |
| LibreOffice / Core | 22 | 18 | 12 |
| 本体 / Core | 22 | 19 | 16 |

文字インベントリは補助指標です。文字順・空白・ヘッダー/フッター・抽出方法・再計算・Coreの対象外機能が影響します。
同一でも罫線、色、配置、下線が正しいとは限りません。差があっても直ちに文字欠落や製品不具合ではありません。
各PDFの全文字・位置・サイズ・色、埋込フォント名と抽出できたフォントbytesのハッシュ、画像・ベクター描画を保持しています。
3組のJSONには文字列、文字位置の対応候補、ページ移動、用紙寸法、画像差率があります。
同一文字の繰返しや抽出順の違いがあるため、位置対応は原セルの同一性を保証しません。

結果は [summary.json](summary.json)、[本体判定](main-verdicts.json)、[Core判定](core-verdicts.json)、
[領域別の指摘15件](findings.json) に記録しました。
[全ページの3組比較プレビュー](gallery.md) と [HTML一覧](index.html) から比較画像を参照できます。
プレビューは閲覧用の縮小画像です。比較計算には同じMuPDFで描画した144dpiの原寸画像を用い、罫線/CFは288dpiも保存しました。
左右の原寸画像、並列、半透明重ね合わせ、差分は `artifacts/libreoffice-comparison/images/<pair>/<case>/page-<number>-<dpi>dpi/` にあります。
ページサイズの相違は白いキャンバスの右下への余白追加だけで扱い、元ページの座標・縮尺を保持しています。
ページ数が異なる場合のページ番号対応は未確定なので、両側の全ページ画像を残しています。

## 主な結果と未解決事項

- **Coreのページ増加は列幅メトリクスで再現（F01）**: `01-japanese`、`02-image`、`08-print-scaling` は本体/Calc 1ページ、Core 2ページです。本体の追加条件 `--layout-font NotoSansJP-Regular.ttf` でもすべて2ページになりました。本体主要結果はCalibriの互換MDW 7px、Coreは指定フォントから丸めた8pxで計画します。フォントの数字advanceは11pt/96dpiで8.14pxです。この追加実験は列幅原因の切分けであり、全描画フォントの一致を証明するものではありません。
- **売上の空欄は数式キャッシュ差（F02）**: `08-print-scaling` のG5:G16は数式に保存値がなく、本体/Coreとも12件の診断と空表示です。Calcは再計算しています。本体の別 `recalculate` 結果では6,000円〜36,800円を確認しました。主要cached結果と置換していません。
- **部分書式は未解決（F09、P1）**: Coreの `richtext` で、赤10pt・黒16pt・青下線のrunがすべて黒10ptになります。`cell-argb` のrun色にも差があります。指示書はCoreでサイズ・色・下線保持を要求しますが、設計資料はrun解釈を本体専用とし、Coreは `CaptureRichText=false` です。通常セルの書式保持とrun書式の契約が曖昧で、**意図した機能差/合格に含めていません**。Coreの新機能をこの検証だけで追加せず、契約と実装の整合が必要な未解決事項として残しました。
- **罫線は既存の独立期待値との違いも記録（F06）**: `border-conflicts` のp1–4は本体/Coreで144dpi画像が完全同一。Calcとは結合セルの部分辺、row/column既定辺、CFの辺クリア後の隣接辺が異なります。入力XMLと既存の `SharedBorderWorkbookTests` / `BorderResolutionTests` に根拠があります。二重辺の4重描画は目視で認めません。Calc画像を既存期待値の代用にはしていません。
- **Coreの意図した差（F07/F10/F11）**: CFの塗り・バー・アイコンを評価しない、通常体への統一、IVS基底文字化、未収録字の欠字、図形/図形内文字の省略を各領域で確認しました。legacyバーの `showValue=0` も適用されず、Coreは元の値を表示します。`actual-shape-text` のセル本文 `anchor` は保持されます。10件はこれらを観測したケースの重複除去数で、フォントメトリクス差や未解決部分書式を含みません。
- **CalcのCF表現差（F08）**: legacyバーはCalcがgradient、本体は契約どおりsolidです。x14バーはCalcのB:E列が描かれず、本体は負数/ゼロ軸/方向を含めて描画します。アイコンも形状・輪郭が異なります。値、位置、負数、ゼロ、showValueを画像/XMLで調査しましたが、percent/percentileや優先順位/stopIfTrueを含む全ルールのExcel実機適合は未確認です。
- **フォント環境差（F14）**: Calcの図形PDFにはCarlito/LiberationSerif/DejaVuSans、帳票にはFrankRuhlHofshi-Bold/LinuxLibertineGB等が埋め込まれます。参照manifestの利用可能フォント一覧だけでは実際のフォント一致を説明できません。主要製品結果の入力フォントは指示の3ファイル、CoreはNoto Sans JP 1個です。
- **ケース名と検証範囲を区別（F13）**: 固定 `shapes.xlsx` にはdrawing partがありません。`cf-phase3-layout-mapping` にもCFルールがなく、通常文字の比較だけです。これらを図形/CF検証の成功に数えません。実際の図形は `actual-shape-text` で比較しています。

今回、独立した根拠で修正方針まで確定できた製品不具合はありません。製品コードへの修正は0件です。
部分書式の保持要件を満たした、全件一致、Excel互換性を確認した、という結論にはしていません。

## Excelで後日確認する入力とページ

| 入力 | ページ/領域 | 確認内容 |
| --- | --- | --- |
| `05-borders` | 両側p1–2、A1:D5 | 過大結合セルと自動列分割。Calcはp1 A:C、製品はp1 Aのみ。製品の500.25ptは有効幅499.276ptを超える。 |
| `06-layout-and-pagination` | p1–4、R9/R10等 | 同じ4ページでも行が別ページへ移動。用紙高・フォント・行高と境界の評価。 |
| `09-cell-border` | Calc p1–2 / 製品p1 | Calcの2ページ目は罫線のみ。60%倍率での線とページの対応。 |
| `border-conflicts` | p2–5 | 部分結合辺、既定辺、CFの明示クリア後の隣接辺。 |
| `actual-shape-text` | p1、5図形 | calloutの回転/位置/折返し/重なり。抽出文字は空白/順序を除き本体とCalcで同じ。 |
| `cf-phase1/2`、`cf-mapped-scale`、`cf-phase3-*` | 各p1 | 塗り、優先順位/stopIfTrue、percent/percentile、負数・ゼロ軸・方向・showValue・アイコン。ルールのないlayout-mappingは除く。 |
| `01`–`06`、`08` | 全ページ、フッター等 | 保存フィールドを製品が表示し、Calcにないヘッダー/フッターの扱い。 |
| `richtext`、`cell-argb` | p1 | 通常セルとrunの書式を分離して確認し、Coreの部分書式契約を確定する。 |
| `japanese-grid-report` | p1 | FitToPages縮尺、折返し、IVS・絵文字のフォント代替とPDF文字抽出。 |

## 実行条件・証跡

[environment.json](environment.json) にOS/.NET/MuPDF/製品DLL/フォントのバージョンとハッシュを保存しました。
全22組の元入力・参照PDF SHA-256は [input-reference-hashes.json](input-reference-hashes.json) で一致しています。
参照の `source_commit` 以降に入力は変わっていないため別コミットの入力の取出しは不要でした。
両版に元ブック順の可視シートを明示指定しました。全22入力のシートはすべて可視でした。
本体はpublic RenderAsyncを呼ぶCLI `render`、CoreはConvertAsyncを呼ぶCLI `core` を使用しました。
Mapping再適用、印刷設定の上書き、ページ/範囲フィルター、trim、手動幅変更はしていません。

[main-cases.json](main-cases.json) / [core-cases.json](core-cases.json) はコマンド、終了コード、stdout/stderr、診断、ページ数、シート状態、入力/PDF/フォントハッシュ、入力の数式・印刷設定・CF領域を含みます。
本体の個別manifestと両版の原PDFは `artifacts/libreoffice-comparison/actual/{main,core}/`、全PDFの文字位置/埋込フォント等は同 `pdf-inspection/` にあります。
比較組別のJSONは `pairs/{libreoffice-main,libreoffice-core,main-core}/` にあります。
[run-artifact-hashes.json](run-artifact-hashes.json) は実行証跡一覧とハッシュです。

追加条件は [investigations.json](investigations.json) に保持しました。
Coreの無指定全シート出力は、今回だけ作った複製の第2シートを非表示にして確認しました。
無指定で2ページ、可視シート明示で1ページとなり契約どおりです。元入力は変えていません。
Core単独consumerとReader単独consumerのrestore/build/publish、禁止依存、AssemblyRef、別プロセスでの初回実行は
[reader-isolation.json](reader-isolation.json) のとおり成功しています。

## 再現と画像の取得

Python依存: PyMuPDF 1.26.6、Pillow 12.3.0、numpy 2.3.5。
ビルド/テスト/レンダリングは出力先を共有するため順次実行します。
参照生成スクリプトは実行せず、通常CIにも追加していません。
既存の凍結A/B/C比較ツール、期待PDF、閾値は変更していません。

```bash
dotnet restore ExcelRenderer.slnx
dotnet build src/ExcelRenderer.Tool -c Release --no-restore -m:1
source scripts/test-fonts.sh
# 再実行では新しいrunディレクトリを使う。既存Core PDFは再利用しない。
python3 scripts/compare-libreoffice.py self-test
python3 scripts/compare-libreoffice.py render
python3 scripts/compare-libreoffice.py compare
python3 scripts/compare-libreoffice.py investigate
python3 scripts/validate-reader-isolation.py --output artifacts/libreoffice-comparison/reader-isolation.json
python3 scripts/compare-libreoffice.py review
# rootを配信してHTMLの原寸画像リンクを開く
python3 -m http.server --directory . 8000
```

新しいrunを指定する場合はすべてに同じ `--run artifacts/<new-run>` を渡し、isolationの出力先も合わせます。
`review` は比較結果に保持済みの人手判定を付ける処理で、新しい製品コミットに無検討で流用するための合否判定ではありません。
保持画像一式はワークスペースの `artifacts/libreoffice-comparison/` にあります。
PRには小さなプレビューを含め、原寸画像は再現手順またはワークスペースの実行証跡アーカイブから取得します。
アーカイブの作成例（大きな一時consumerを除外）:

```bash
tar --exclude=isolation-consumers -czf artifacts/libreoffice-comparison-evidence.tar.gz -C artifacts libreoffice-comparison
```

## チェック結果

- Release CLIビルド、solutionのRebuild: 警告0・エラー0。
- 比較スクリプトの変異検出: 文字欠落・線欠落・余分なページの3件すべて検出（[結果](mutation-self-tests.json)）。
- Reader/Core単独consumer: 成功。Coreの未指定時の非表示シートを含む出力: 成功。
- Namespaceチェック: 716宣言とcompiler shim 2件を確認。
- solutionテスト: **2886件成功、失敗0・skip0**。禁止依存監査: 24 restore graph / 726 AssemblyRef inventory成功。最終証跡整合を含む実測は [checks.json](checks.json) に記録。

初回のsolutionテストはsandboxがテスト通信のlocalhost socketを拒否して中断しました。
ネットワーク権限を有効にした同じコマンドで再実行し、その結果と両方のログを保持しました。
Coreの製品コードは変更していないため、専用coverage/performance gateを新しい変更の受入根拠としては要求していません。
