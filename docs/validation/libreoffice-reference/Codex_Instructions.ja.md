# LibreOffice PDFを使ったExcelRendererの検証指示

## 目的と前提

最新のmainを取得し、この指示書と同じディレクトリにある `manifest.json` / `pdf/` を使って、ExcelRenderer本体版とExcelRenderer.Core版の両方のPDF出力を検証する。全22ケースを両版で実行し、LibreOffice対本体、LibreOffice対Core、本体対Coreの3組を比較する。比較結果を調査し、根拠のある不具合を修正してPRにまとめる。

参照PDFはLibreOffice Calcの暫定比較資料であり、Windows Excelの正解PDFではない。Excel実機確認は後日行う。LibreOfficeと違うという理由だけで、ExcelRendererの仕様を変更しない。LibreOffice参照PDFの再生成や差し替えで差分を解消しない。

## 1. 開始と入力の固定

1. `git fetch origin` を実行し、最新 `origin/main` から作業ブランチを作る。AGENTS.mdと関連契約を読む。他の進行中PRの変更を混ぜない。
2. 検証開始時のコミットSHA、OS、.NET SDK、PDFラスタライザーのバージョンを記録する。
3. manifestの各 `input` / `pdf` のSHA-256を検証する。入力が更新されていた場合、そのケースは「入力変更により比較不可」と明記し、参照を黙って更新しない。必要なら `source_commit` の入力を別ディレクトリに取り出して比較する。最新製品コードで、その固定入力をレンダリングする。
4. 全ケースを対象とする。優先順は罫線競合、折り返し・改ページ・印刷倍率、リッチテキスト、条件付き書式phase1/2/3、画像・図形、日本語・通常サンプル。テンプレートにMappingを再適用しない。`cf-mapped-scale` は既に展開された入力をそのまま使う。

## 2. 製品PDFを生成する

- `dotnet restore ExcelRenderer.slnx` 後、CLIをReleaseでビルドする。必要な.NET 10 SDK / Linuxネイティブ依存を環境に用意する。ビルドとレンダリングを同時実行しない。
- 各入力から本体版とCore版の2種類を必ず生成する。本体は `src/ExcelRenderer.Tool` の `render` コマンド（public RenderAsync経由）、Coreは同CLIの `core` コマンド（Core.ExcelConverter.ConvertAsync経由）を使う。出力はそれぞれ `artifacts/libreoffice-comparison/actual/main/<id>.pdf` と `actual/core/<id>.pdf` に分け、参照PDFの場所には書かない。失敗したケースも記録し、片方だけで検証を完了しない。
- フィルター、印刷範囲の上書き、ページ選択、trim、縮小して1ページ化、手動の列幅/行高変更を行わない。manifestのシート状態と各版の既定動作を確認する。Coreは未指定時に非表示シートも含めて全シートを出力するため、Calcが出力した可視シートをブック順に明示指定して比較条件を揃える。本体にも同じシート一覧を指定する。別途、Coreの未指定時の全シート出力は契約どおりか確認し、非表示シートの追加を改ページ不具合にしない。可視シートがない場合は無指定にすり替えず、比較対象外の理由を記録する。
- 本体の条件付き書式は有効のままにする。Coreでも条件付き書式を含む同じ入力を処理し、通常書式・値・罫線・ページ構成を検証する。Coreが条件付き書式を評価しないこと自体は契約どおりと判定する。
- 本体は `--layout-warnings --detect-unsupported-features --verbose --manifest <case.json>` で診断も保存する。Coreは `--verbose` と標準出力/標準エラー、終了コード、ページ数、入力/PDF/フォントハッシュをラッパー側のJSONへ保存する。Coreに存在しない `--manifest` 等を渡さない。未対応機能警告を消して成功扱いしない。
- 本体は最初に `--formula-mode cached` で実行する。Coreは保存済み計算値専用なので同等の設定追加は不要であり、`--formula-mode` を渡さない。LibreOfficeは読み込み時に再計算し得るので、値の差があればキャッシュ、式、再計算の有無を調査し、必要なケースだけ `recalculate` を別の結果として追加する。これはExcel数式の正解値検証の代用ではない。Coreとの直接比較はcachedの本体結果を使い、本体recalculateの結果と混同しない。

コマンドの形（現行CLIのhelpで最終確認し、複数シートは両版に同じ `--sheet` を追加すること）:

```bash
dotnet build src/ExcelRenderer.Tool -c Release
source scripts/test-fonts.sh
dotnet run --project src/ExcelRenderer.Tool -c Release --no-build -- render \
  tests/ExcelRenderer.Tests/SampleInputs/01-japanese.xlsx \
  --format pdf --output artifacts/libreoffice-comparison/actual/main/01-japanese.pdf \
  --formula-mode cached --font-policy requested --no-system-fonts \
  --font-file third_party/NotoSansJP/NotoSansJP-Regular.ttf \
  --font-file third_party/IPAmjMincho/ipamjm.ttf \
  --font-file tests/ExcelRenderer.Tests/Fonts/NotoSansMono-Regular.ttf \
  --fallback-font 'Noto Sans JP' --fallback-font IPAmjMincho \
  --layout-warnings --detect-unsupported-features --verbose \
  --manifest artifacts/libreoffice-comparison/actual/main/01-japanese.manifest.json

# Coreは1個の静的TrueType通常体を明示指定する。出力は新規ファイル必須。
dotnet run --project src/ExcelRenderer.Tool -c Release --no-build -- core \
  tests/ExcelRenderer.Tests/SampleInputs/01-japanese.xlsx \
  --output artifacts/libreoffice-comparison/actual/core/01-japanese.pdf \
  --font-file third_party/NotoSansJP/NotoSansJP-Regular.ttf --verbose
```

再実行時はCoreの既存出力を再利用して成功扱いせず、今回作成した出力だけを削除するか、run ID別の新規ディレクトリを使う。入力・参照PDFは削除しない。

### Coreの対応範囲と比較の判定

最新版の `docs/core.md` / `docs/core.ja.md` / `docs/core-source-map.md` を根拠に、各ケース・領域の対応範囲を先に記録する。入力全体を対象外にせず、共通対応部分の検証を続ける。

| 項目 | 本体版 | Core版の判定基準 |
|---|---|---|
| 値、通常書式、結合、罫線、折り返し、印刷設定、ヘッダー/フッター | 対応する契約で検証 | 共通対応部分として必ず検証。欠落・重複・罫線不整合などを調査 |
| 数式 | cachedを基本とし、必要時のみ別途recalculate | 保存済み値のみ。キャッシュ欠落は空表示と診断を確認。再計算は要求しない |
| 条件付き書式・データバー・アイコン | 対応範囲を検証 | 評価しない。元の通常書式と値を確認し、CF由来の差は意図した機能差 |
| フォント、リッチテキスト、IVS | 本体の対応範囲を検証 | 指定した通常体1個に統一。太字/斜体の合成・フォールバックなし。通常セル全体のサイズ・色・下線の保持と、IVSの基底文字化を検証。Coreのrichtext部分書式は未対応で、run本文を連結し通常セル書式で描画する。run個別の色・サイズ・下線は要求しない |
| 図形、吹き出し、図形内文字、リンク注釈 | 対応範囲を検証 | 出力対象外。セル本文やリンク表示文字まで消えていないかは検証 |
| 画像 | 変形・ページ跨ぎ等の本体契約で検証 | 元画像全体を配置矩形へ描画し、左上位置のページだけに出力。crop/回転/反転、ページ跨ぎ分割・繰り返しを要求しない |
| セル回転 | 本体の対応範囲を検証 | 回転を無視する契約として扱う |

Coreの単一フォントによる文字幅・列幅・折り返し・改ページの差は自動的に不具合としない。一方、すべてをフォント差として除外せず、指定フォントのメトリクス、セル座標、印刷設定から妥当性を調査する。

本体対Coreは共通対応領域で直接比較し、必要なら**追加の本体単一フォント条件**を作って原因を切り分ける。本体の既定変換やCF有効の主要結果を置き換えない。APIで指定フォント・通常体を確実に揃えたことを計測・埋込フォントで確認できる場合のみフォント一致とみなし、複数フォントを登録しただけで同条件とは判断しない。機能差がある領域も全体画像と差分は残し、契約・セル/領域を指定して分類する。

CLI経由のCore実行に加え、既存 `scripts/validate-reader-isolation.py` のCore単独consumerを実行し、Coreが本体・OpenXml・Formulas・Fonts・Toolへの依存なしでPDFを生成できることも確認する。CLI全体が本体に依存することと、Coreライブラリ自体の依存を混同しない。

参照のフォント環境はmanifestに記録済み。Calibri/Meiryo等の実フォントは含まれておらず、Calc側で代替されている。利用可能なフォントを合わせても、両実装の代替選択と太字合成が一致する保証はない。PDFの埋込フォントも確認し、差が文字メトリクスによるものか識別する。製品既定のbundledポリシーとの差を調べる場合は別ケースにする。元XLSXのフォントを変更しない。

## 3. 比較ツールと証跡

`tools/ExcelRenderer.PdfComparison` と `scripts/compare-pdf-backends.py` は凍結されたA/B/C比較専用である。既存の正解PDF、例外、閾値を上書きせず、今回用の独立した比較スクリプトを追加する。既存のラスタライズ・差分実装は参考にしてよい。

各ケースについて、**LibreOffice対本体、LibreOffice対Core、本体対Core**を別々にJSONと閲覧用HTML/Markdownへ記録する。実行単位に `engine`（main/core）、比較単位に `pair` とケースIDを付け、片方の結果で上書きしない。各組で次を保存する。

1. 入出力ハッシュ、変換成功/失敗、シート情報、診断、PDFページ数。
2. ページごとの用紙寸法（pt）。ページ数や寸法が違えば構造差として扱い、黙ってリサイズして比較しない。
3. 全ページの文字列と文字位置。空白・文字順の抽出差は別扱いとし、欠落/重複、別ページへの移動、数値・日付・表示形式の差を確認する。リッチテキストと図形内文字も対象。本体/Coreの機能差は上記の対応範囲に従って分類する。
4. 両PDFを同じラスタライザーで144dpiに画像化し、並列画像・半透明重ね合わせ・差分を全ページ保存する。罫線と小さなアイコンは288dpiの拡大も作る。ページをずらす、拡縮する、都合の悪い領域を隠す処理で差を消さない。
5. ページサイズが違う場合は余白付きの比較キャンバスを使ってよいが、元サイズと配置を保持し、スケール補正しない。ページ対応が不明なら両側の全ページを別々に表示する。
6. 画像差分率は補助情報。文字のアンチエイリアス、フォント代替、Calcのアイコン表現だけでCIを失敗させない。参照と一致しただけでExcel互換と断言しない。

罫線は隣接辺の二重描画、4重線、線の途切れ、結合セル周辺の欠けを確認する。条件付き書式は塗り・優先順位・stopIfTrue、percent/percentile、負数とゼロ軸、showValue、アイコン位置と値の重なりを調べる。phase3のx14ルールがCalcで未対応/異なる可能性を考慮する。

## 4. 差の分類と修正

各差に、比較組、実行エンジン、ケースID、ページ、セル/領域、期待値、実測値、画像、根拠、優先度、対応を付ける。

- **製品不具合**: 入力XML・仕様・既存の独立した期待値などで期待動作を説明できる。修正と再現テストを追加する。
- **Coreの契約上の機能差**: 条件付き書式・図形の非対応、通常体1個への統一、IVS基底文字化、画像の扱い等。契約箇所と影響領域を示す。検証未実施や本来対応する領域の欠落をこの分類で隠さない。
- **LibreOfficeの未対応/挙動差**: 製品側を追従させない。入力ルールと証拠を記録する。
- **フォント/環境/再計算差**: 原因を分離し、製品不具合と混同しない。
- **Excel実機で要確認**: 両者のどちらが正しいか決められない。保留リストへ残す。
- **入力変更/未実行/ツール失敗**: 未検証として明示する。

帳票描画の欠落・重複、明確な印刷設定の無視、例外など根拠が確定できた不具合を優先してまとめて修正する。文字幅などはExcel未確認のまま一律の補正係数で修正しない。共有アルゴリズムはCoreの責務に従い修正し、Coreに条件付き書式・図形を追加しない。

## 5. 回帰確認と完了条件

- 比較スクリプトが文字欠落、線の欠落、余分なページを検出することを小さな変異入力で確認する。画像をそのまま期待値にするだけのテストにしない。
- 製品コード変更時はAGENTS.mdの再ビルド（警告0）・solutionテスト・namespaceチェックを実施する。Core変更はCore専用のcoverage/performance gateも通す。公開API/依存/配布を触った場合は該当契約の検証も行う。
- 修正後に本体/Coreの全22ケースを再生成し、3組を再比較して、修正前後の証跡を保持する。参照PDFと元入力は変更しない。
- `docs/validation/libreoffice-comparison/` に結果要約、main/core別の全ケースの判定JSON、3組の比較画像、環境とハッシュ一覧を保存する。実行成功数・生成PDF数・ページ数・失敗/未実行数・機能差の件数をエンジン別に集計する。正常時は22入力×2版の44PDFと、各ケースの3組の比較記録があることを確認する。失敗や入力更新は理由付きで残し、欠番を成功扱いしない。大きな中間画像はartifacts配下に置き、取得方法を記す。
- READMEの「実行済み」「未検証」を明確にし、Excel一致を確認したという表現を避ける。参照生成は通常CIから起動しない。
- 最後に「本体/Coreそれぞれのケース数とページ数」「3組の比較結果とCoreの意図した機能差」「確定不具合と修正」「Calc/フォント等の差」「Excelで後日確認すべき入力とページ」「テスト結果」をPRに記す。判定不能なケースを残してもよいが、全件一致や全件成功としてまとめない。
