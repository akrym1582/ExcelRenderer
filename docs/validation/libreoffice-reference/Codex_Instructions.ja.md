# LibreOffice PDFを使ったExcelRendererの検証指示

## 目的と前提

最新のmainを取得し、この指示書と同じディレクトリにある `manifest.json` / `pdf/` を使って、ExcelRenderer本体のPDF出力を検証する。比較結果を調査し、根拠のある不具合を修正してPRにまとめる。

参照PDFはLibreOffice Calcの暫定比較資料であり、Windows Excelの正解PDFではない。Excel実機確認は後日行う。LibreOfficeと違うという理由だけで、ExcelRendererの仕様を変更しない。LibreOffice参照PDFの再生成や差し替えで差分を解消しない。

## 1. 開始と入力の固定

1. `git fetch origin` を実行し、最新 `origin/main` から作業ブランチを作る。AGENTS.mdと関連契約を読む。他の進行中PRの変更を混ぜない。
2. 検証開始時のコミットSHA、OS、.NET SDK、PDFラスタライザーのバージョンを記録する。
3. manifestの各 `input` / `pdf` のSHA-256を検証する。入力が更新されていた場合、そのケースは「入力変更により比較不可」と明記し、参照を黙って更新しない。必要なら `source_commit` の入力を別ディレクトリに取り出して比較する。最新製品コードで、その固定入力をレンダリングする。
4. 全ケースを対象とする。優先順は罫線競合、折り返し・改ページ・印刷倍率、リッチテキスト、条件付き書式phase1/2/3、画像・図形、日本語・通常サンプル。テンプレートにMappingを再適用しない。`cf-mapped-scale` は既に展開された入力をそのまま使う。

## 2. 製品PDFを生成する

- `dotnet restore ExcelRenderer.slnx` 後、CLIをReleaseでビルドする。必要な.NET 10 SDK / Linuxネイティブ依存を環境に用意する。ビルドとレンダリングを同時実行しない。
- 各入力を `src/ExcelRenderer.Tool` の `render` コマンド（public RenderAsync経由）でPDFにする。出力は `artifacts/libreoffice-comparison/actual/<id>.pdf` とし、参照PDFの場所には書かない。
- フィルター、印刷範囲の上書き、ページ選択、trim、縮小して1ページ化、手動の列幅/行高変更を行わない。ブック全体の可視シートを元の順序で処理する。非表示シートの扱いが違えば先にその差を記録する。
- 本体の条件付き書式は有効のままにする。Coreはその機能の比較対象ではない。
- `--layout-warnings --detect-unsupported-features --verbose --manifest <case.json>` で診断も保存する。未対応機能警告を消して成功扱いしない。
- 最初は `--formula-mode cached` で実行する。LibreOfficeは読み込み時に再計算し得るので、値の差があればキャッシュ、式、再計算の有無を調査し、必要なケースだけ `recalculate` を別の結果として追加する。これはExcel数式の正解値検証の代用ではない。

コマンドの形（現行CLIのhelpで最終確認すること）:

```bash
dotnet build src/ExcelRenderer.Tool -c Release
source scripts/test-fonts.sh
dotnet run --project src/ExcelRenderer.Tool -c Release --no-build -- render \
  tests/ExcelRenderer.Tests/SampleInputs/01-japanese.xlsx \
  --format pdf --output artifacts/libreoffice-comparison/actual/01-japanese.pdf \
  --formula-mode cached --font-policy requested --no-system-fonts \
  --font-file third_party/NotoSansJP/NotoSansJP-Regular.ttf \
  --font-file third_party/IPAmjMincho/ipamjm.ttf \
  --font-file tests/ExcelRenderer.Tests/Fonts/NotoSansMono-Regular.ttf \
  --fallback-font 'Noto Sans JP' --fallback-font IPAmjMincho \
  --layout-warnings --detect-unsupported-features --verbose \
  --manifest artifacts/libreoffice-comparison/actual/01-japanese.manifest.json
```

参照のフォント環境はmanifestに記録済み。Calibri/Meiryo等の実フォントは含まれておらず、Calc側で代替されている。利用可能なフォントを合わせても、両実装の代替選択と太字合成が一致する保証はない。PDFの埋込フォントも確認し、差が文字メトリクスによるものか識別する。製品既定のbundledポリシーとの差を調べる場合は別ケースにする。元XLSXのフォントを変更しない。

## 3. 比較ツールと証跡

`tools/ExcelRenderer.PdfComparison` と `scripts/compare-pdf-backends.py` は凍結されたA/B/C比較専用である。既存の正解PDF、例外、閾値を上書きせず、今回用の独立した比較スクリプトを追加する。既存のラスタライズ・差分実装は参考にしてよい。

各ケースで次をJSONと閲覧用HTML/Markdownに記録する。

1. 入出力ハッシュ、変換成功/失敗、シート情報、診断、PDFページ数。
2. ページごとの用紙寸法（pt）。ページ数や寸法が違えば構造差として扱い、黙ってリサイズして比較しない。
3. 全ページの文字列と文字位置。空白・文字順の抽出差は別扱いとし、欠落/重複、別ページへの移動、数値・日付・表示形式の差を確認する。リッチテキストと図形内文字も対象。
4. 両PDFを同じラスタライザーで144dpiに画像化し、並列画像・半透明重ね合わせ・差分を全ページ保存する。罫線と小さなアイコンは288dpiの拡大も作る。ページをずらす、拡縮する、都合の悪い領域を隠す処理で差を消さない。
5. ページサイズが違う場合は余白付きの比較キャンバスを使ってよいが、元サイズと配置を保持し、スケール補正しない。ページ対応が不明なら両側の全ページを別々に表示する。
6. 画像差分率は補助情報。文字のアンチエイリアス、フォント代替、Calcのアイコン表現だけでCIを失敗させない。参照と一致しただけでExcel互換と断言しない。

罫線は隣接辺の二重描画、4重線、線の途切れ、結合セル周辺の欠けを確認する。条件付き書式は塗り・優先順位・stopIfTrue、percent/percentile、負数とゼロ軸、showValue、アイコン位置と値の重なりを調べる。phase3のx14ルールがCalcで未対応/異なる可能性を考慮する。

## 4. 差の分類と修正

各差に、ケースID、ページ、セル/領域、期待値、実測値、画像、根拠、優先度、対応を付ける。

- **製品不具合**: 入力XML・仕様・既存の独立した期待値などで期待動作を説明できる。修正と再現テストを追加する。
- **LibreOfficeの未対応/挙動差**: 製品側を追従させない。入力ルールと証拠を記録する。
- **フォント/環境/再計算差**: 原因を分離し、製品不具合と混同しない。
- **Excel実機で要確認**: 両者のどちらが正しいか決められない。保留リストへ残す。
- **入力変更/未実行/ツール失敗**: 未検証として明示する。

帳票描画の欠落・重複、明確な印刷設定の無視、例外など根拠が確定できた不具合を優先してまとめて修正する。文字幅などはExcel未確認のまま一律の補正係数で修正しない。共有アルゴリズムはCoreの責務に従い修正し、Coreに条件付き書式・図形を追加しない。

## 5. 回帰確認と完了条件

- 比較スクリプトが文字欠落、線の欠落、余分なページを検出することを小さな変異入力で確認する。画像をそのまま期待値にするだけのテストにしない。
- 製品コード変更時はAGENTS.mdの再ビルド（警告0）・solutionテスト・namespaceチェックを実施する。Core変更はCore専用のcoverage/performance gateも通す。公開API/依存/配布を触った場合は該当契約の検証も行う。
- 修正後に全ケースを再比較し、修正前後の証跡を保持する。参照PDFと元入力は変更しない。
- `docs/validation/libreoffice-comparison/` に結果要約、全ケースの判定JSON、重要な比較画像、環境とハッシュ一覧を保存する。大きな中間画像はartifacts配下に置き、取得方法を記す。
- READMEの「実行済み」「未検証」を明確にし、Excel一致を確認したという表現を避ける。参照生成は通常CIから起動しない。
- 最後に「確認したケース数とページ数」「確定不具合と修正」「Calc/フォント等の差」「Excelで後日確認すべき入力とページ」「テスト結果」をPRに記す。判定不能なケースを残してもよいが、全件一致や全件成功としてまとめない。
