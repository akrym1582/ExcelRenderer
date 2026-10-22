# 本体とCoreの使い分け・直接比較

[English](main-core-parity.md) · [診断と出力](diagnostics.ja.md) · [Coreの契約](core.ja.md)

複数フォント、部分書式、条件付き書式、図形、リンク、再計算、範囲・ページ選択、trim、
PNG/SVG/Markdown/PostScriptが必要なら本体を使います。Coreは、指定した通常体TrueType
1個と保存済み値でPDFを作る用途です。Core DLLは本体NuGetに同梱され、単独Coreパッケージはありません。

| 機能 | 関係 | 条件・意図した差 |
| --- | --- | --- |
| 値・数値書式・セル・罫線・塗り・印刷 | 共通 | 同じシートを同じ順に明示指定 |
| 文字寸法・列幅 | 設定により一致 | 同じ物理フォントを描画に使用し、元NormalのMDW情報を保持 |
| 数式 | 設定により一致 | 本体CachedValue。Coreは保存値のみ。キャッシュ欠落は空文字と診断 |
| セル全体のサイズ・色・下線 | 共通 | 通常体の文字 |
| 太字・斜体・部分書式 | 意図した差 | Coreは通常体1個、runを連結しセル全体の書式で表示 |
| 条件付き書式・図形・PDFリンク | 意図した差 | Coreは生成しない。セル表示文字は残る |
| IVS・fallback | 意図した差 | Coreはselectorを除去。フォント探索・fallbackなし |
| 基本画像 | 共通 | 加工なし、開始点が1ページ内、タイトル繰返し外 |
| crop・回転・反転・境界跨ぎ・タイトル内画像 | 意図した差 | Coreは元画像全体を引き伸ばし開始点ページだけに出力 |
| セル回転 | 意図した差 | Coreは無視 |
| シート未指定 | 現行APIは一致 | 両方とも非表示を含むブック順。比較では明示指定 |

[MainCoreParityTests](../tests/ExcelRenderer.Tests/MainCoreParityTests.cs)は同一XLSX bytesから
別々のStreamを作り公開APIを呼びます。Core独立テストへの本体・共有fixture依存追加はありません。
本体はOS探索とフォントパックを無効化し、PreferRequested、face 0、fallbackもNoto Sans JPに固定。
PDF埋め込みフォントのfamily、glyph ID、非ヒンティング幅、字形輪郭を元フォントと照合し、
入力・フォントSHA-256をケース別に記録します。元Normalを描画フォントに書き換えて列幅を一致させません。

| ケースID | 入力・比較項目 | 独立期待値 |
| --- | --- | --- |
| values | 日本語/ASCII、整数・小数・負数・日付、型付き数式保存値・欠落 | 123、12.50、-42、2024-01-01、saved、TRUE、#DIV/0!、欠落診断 |
| text | 14pt青下線、中央/上下中央、折返し、右/上ShrinkToFit | 複数baseline、縮小サイズ、下線の描画 |
| dimensions-match / dimensions-mismatch | 元Normal一致/Calibri、明示列幅20・行高30pt、結合、非表示行列 | 非表示文字なし、左余白0.5inchに対応する36–40ptの開始位置 |
| borders | 赤単線、二重線、破線、黄塗り、結合の部分辺 | 黄塗り・0.5pt線。競合辺の独立期待値は既存固定罫線監査も利用 |
| portrait / landscape | A4と余白 | 幅595.276 / 841.890pt（mm×72/25.4） |
| scale / fit | 75% / 500ptの2行を1ページFit | 75%は8.25pt、Fitは1ページへ縮小 |
| breaks / titles / areas | 1行目後の改ページ / 1行目繰返し / 2印刷範囲 | 各2ページ、タイトルは両ページ |
| empty | 空シート | 1ページ |
| selection | second、data、選択外の第3シート | 2ページ、指定順、選択外文字なし |
| images | B2開始の3×3赤PNG | 1画像、手計算B2座標(156, 66, 158.25, 68.25)ptと赤画素 |
| header-footer | STATIC / PAGE &P | STATICとPAGE 1 |
| defaults | 行/列既定書式、明示styleなしセル | ROW/COLUMNと黄塗り |
| border-conflict | 隣接セルの単線/二重線競合 | 勝った二重線の0.75ptストローク |
| image-overlap | 同じB2の赤/青画像 | 2画像、配置・画素・順序一致 |

PdfPigでページ数・順序・寸法、重複も含む文字順・baseline・サイズ・色、描画順を保持した
罫線/塗りのパス・幅・色・コマンド、画像位置・データを比較します。位置・サイズの許容差は
既存の[ReaderAnchorRegressionTests](../tests/ExcelRenderer.Tests/ReaderAnchorRegressionTests.cs)の
幾何検証と同じ0.01pt（writerのbox正規化は0.001pt）。ページ・文字に許容差はありません。
パスコマンドは数値表現まで一致を要求し、別途実行する既存罫線監査の0.002pt上限も維持します。描画しないclipパスは罫線本数には数えません。
clipの影響や重なり順はLinuxで既存 `libreoffice_regression.compare` の意味比較と
144/288dpi画素完全一致でも確認します。位置合わせやマスクは使いません。

文字欠落、座標ずれ、罫線欠落/追加、余分なページを比較器が拒否するテストもあります。
[期待差テスト](../tests/ExcelRenderer.Tests/MainCoreExpectedDifferenceTests.cs)は部分書式、回転、
IVS、リンク、条件付き書式を属性/領域単位で確認し、Coreの基準出力と本体の変更外セルも検証します。
画像・単一フォント・数式の広い契約は既存テストも維持します。本体とCoreの一致はExcel完全互換を意味しません。
既存44PDF固定比較・LibreOffice参照・過去記録は別に維持します。

```sh
# Linuxでは同じshellで先に実行
source scripts/test-fonts.sh
dotnet test tests/ExcelRenderer.Tests -c Release --filter 'FullyQualifiedName~MainCoreParityTests|FullyQualifiedName~MainCoreExpectedDifferenceTests'
python3 scripts/check-main-core-parity.py
```

通常solutionテストに意味比較を含め、Windows CIでも実行。Linuxでは画素比較も実行します。
両PDF・入力・比較JSON・差分画像をCIの `SampleOutputs/MainCoreParity` に保存します。
[実行記録](validation/main-core-parity/README.md)も参照してください。

ファイル変換の実コードは[本体](../samples/ExcelRenderer.Http.Sample/PdfFileExample.cs)と
[Core](../samples/ExcelRenderer.Core.Sample/Program.cs)。いずれも一意な隣接一時ファイルへ生成し、
閉じてから置換します。失敗時は既存成果物を保持し、一時ファイルを削除。Ctrl+Cにも対応します。
電源断耐性は保証しません。[起動コマンド](../samples/ExcelRenderer.Http.Sample/README.ja.md)を参照。
リポジトリ内はProjectReference、公開パッケージ利用者はExcelRendererパッケージを参照します。

期待差の既存独立テストも再利用します: [Core PdfTests](../tests/ExcelRenderer.Core.Tests/PdfTests.cs)（通常体化）、[PageAndImageTests](../tests/ExcelRenderer.Core.Tests/PageAndImageTests.cs)（画像開始点ページ・加工無視・図形省略）、[SavedFormulaTests](../tests/ExcelRenderer.Core.Tests/SavedFormulaTests.cs)（保存値のみ）、[本体再計算](../tests/ExcelRenderer.Tests/FormulaRecalculationTests.cs)。
