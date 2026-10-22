# Coreの代替フォントによるページ増加の修正

本体とCoreで元の列幅メトリクスの扱いが異なっていたため、Coreの `01-japanese`、`02-image`、
`08-print-scaling` が各1ページから2ページに増えていました。
Coreの描画用フォントで元Normalの列幅を再計算しないよう修正しました。
印刷倍率、用紙、余白、セル幅の入力値、選択シートを上書きしていません。

## 原因と修正

3入力の元NormalはCalibriです。指定したNoto Sans JPのMDWは8pxですが、本体は
元フォントを取得できない場合に7pxの互換MDWを使います。修正前のCoreは、元のファミリー名を
確認せずに描画用のNotoの8pxで保存列幅を展開していました。

Coreの `SingleFontMetrics` は次のように処理します。

- 元Normalと指定フォントの物理ファミリー名が一致する場合は、従来どおり指定snapshotと元NormalサイズでMDWを実測します。名前の大文字小文字は区別しません。
- 不一致またはNormal情報がない場合は、本体と同じ7px/96dpi互換MDWを使い、`MaximumDigitWidthFallback` を通知します。
- 文字の計測、折返し、PDF描画は引き続き指定フォント1個を使います。OS探索や追加フォントへの依存は加えていません。

これは本体と互換な元フォント不明時の方針で、利用できない元フォントの正確なExcel寸法を復元するものではありません。
すべてのブックのページ数を本体やCalcと同じにする保証ではなく、元のファミリーと描画代替を混同した3ケースを解消する修正です。
公開API、配布設定、Coreの依存関係は変更していません。

## 比較結果

| ケース | 修正前Core | 修正後Core | 本体 |
| --- | ---: | ---: | ---: |
| 01-japanese | 2 | 1 | 1 |
| 02-image | 2 | 1 | 1 |
| 08-print-scaling | 2 | 1 | 1 |

全22入力を本体/Coreで再生成し、LibreOffice対本体・LibreOffice対Core・本体対Coreの全66組を比較しています。
再生成結果は本体22件・31ページ、Core22件・31ページで生成失敗0件です。本体/Coreのページ数は全22件で一致し、本体の全22件は修正前と144dpi画像が完全同一でした。Coreの10件も修正前と画像同一で、その他のケースには列幅方針変更による配置差があります。

実測の実行数・ページ数・比較指標は [summary.json](summary.json)、修正前後の比較は [before-after.json](before-after.json) を参照してください。
[本体判定](main-verdicts.json)、[Core判定](core-verdicts.json)、[領域別指摘](findings.json)、
[全ページ比較画像](gallery.md) を保存しています。
修正前の31/34ページの結果は[親レポート](../README.md)と元のartifactsに保持しています。

この追補でF01を修正済みとしました。ユーザーの追加指示（2026-10-22）により、Coreのrichtext部分書式は未対応でよいことが確定し、F09は契約上の差へ分類しました。通常セル全体のサイズ・色・下線と、runの部分書式をドキュメントで区別しています。Calcのx14/gradient/アイコン表現、フォント環境、
過大結合セル・罫線・図形・ヘッダーフッター等のExcel実機確認事項は継続しています。
ページ数の一致を描画全体の一致やExcel一致として扱っていません。

## 回帰確認と証跡

列幅10の元Calibri/不明フォントに、異なる2個の描画フォントを指定しても52.5ptを維持すること、
一致するNoto Sans JPは11ptでMDW 8・20ptで15を使うこと、Normal情報欠落で警告することをテストしています。
3つの元サンプルをCore専用fixtureで実際に変換し、ページ数1とPDFの読取を確認しました。修正前後の比較は[before-after.py](before-after.py)で再現できます。
性能fixtureには元Calibriの代替を示す警告が1件出ることを明示検証しています。
新しい警告を無視したり、ページ数や画像数等のチェックを緩めたりしていません。

solution再ビルド、solutionテスト、Core専用coverage/performance gate、namespace/依存監査、
Core/Reader単独consumerの結果は [checks.json](checks.json) に記録しています。solutionテストは2,890件成功（失敗0・skip0）、Core coverageは405件成功・行82.9%/分岐71.55%、Core performanceは8件成功、Debug/Release再ビルドは警告0・エラー0でした。
環境・製品DLL/フォントハッシュは [environment.json](environment.json)、固定入力/参照ハッシュは
[input-reference-hashes.json](input-reference-hashes.json) にあります。
今回も全入力・参照PDF・参照manifestを維持しています。

原PDF、144/288dpi原寸画像、文字位置、ログは
`artifacts/libreoffice-comparison/core-pagination-fix/` に保持し、
[run-artifact-hashes.json](run-artifact-hashes.json) に一覧とハッシュを保存しています。
旧結果と混在させず、新規出力で実行しました。
比較スクリプトの仕様と画像の読み方は[親レポート](../README.md)を参照してください。

```bash
source scripts/test-fonts.sh
python3 scripts/compare-libreoffice.py self-test --run artifacts/libreoffice-comparison/core-pagination-fix
python3 scripts/compare-libreoffice.py render --run artifacts/libreoffice-comparison/core-pagination-fix
python3 scripts/compare-libreoffice.py compare --run artifacts/libreoffice-comparison/core-pagination-fix --report docs/validation/libreoffice-comparison/core-pagination-fix
# 補助実験と単独consumerも新規run配下に保存してからreviewを実行
python3 scripts/compare-libreoffice.py investigate --run artifacts/libreoffice-comparison/core-pagination-fix
python3 scripts/validate-reader-isolation.py --output artifacts/libreoffice-comparison/core-pagination-fix/reader-isolation.json
python3 scripts/compare-libreoffice.py review --run artifacts/libreoffice-comparison/core-pagination-fix --report docs/validation/libreoffice-comparison/core-pagination-fix
```

## 罫線の追加調査

ユーザーの追加依頼により、`border-conflicts` の入力XMLと既存独立期待値、実際のPDF線を再照合しました。
[数値監査](border-audit.json)では**本体/Coreの全5シート・各23ストロークが期待値と一致し、重複は0件**でした。
この比較で製品が既存期待値に違反していたのではなく、Calc参照と製品の既存期待値の間に違いがあります。
罫線コードをCalcに合わせる根拠は確認できなかったため、今回の罫線アルゴリズム変更は0件です。

| シート | XML/独立契約から確認した内容 | 製品 | Calcでの相違 |
| --- | --- | --- | --- |
| Double | 隣接Doubleを一つへ解決。境界±0.75pt、幅0.75ptを2本 | 3境界・6ストローク、4重描画なし | 線幅・間隔・列幅が異なる |
| Colors | 同順位なら左セル右辺を優先し白0.5pt。別のDoubleと太線2ptも保持 | 4ストローク、白辺を含め一致 | 白い共有辺が黒く、太線を同一位置で2回描画 |
| Merged A1:B2 | 左/上はA1分だけ、右上Double、右下/下はB2分だけを保持 | 6ストローク、空白区間を補完しない | 外周へ辺を伸ばし、右Doubleを2回描画。製品契約の右下/部分下辺と異なる |
| Defaults | 行1/2の下Double、行3は列既定の右Double | 6ストローク、全座標一致 | 下Doubleの重複と列既定右辺の相違 |
| Conditional | 本体はA1右辺をCFで消去し、隣接B1左0.5ptを保持。Coreは通常2pt | 両版それぞれ1ストローク、契約どおり | 参照には線がない |

`audit-libreoffice-borders.py` は製品モデル/レイアウトAPIを使わず、固定XMLの48pt列・30pt行・18pt余白から手計算した位置と、既存の暫定罫線契約を使います。
独立したMuPDFでPDFの位置・長さ・幅・色を読み、0.002ptの読取許容差で照合します。
線の欠落、重複、誤色、端点の移動、誤った太さの5変異をすべて検出し、出力順変更は許容することも確認しました。
Calcの重複描画はPDF内で実際に同じ線を複数回命令しているという観測であり、Excel仕様の判定ではありません。
白/黒の競合順位、結合辺の補完、既定書式のExcel実機適合については、既存暫定契約の制限を引き続き明示します。

```bash
python3 scripts/audit-libreoffice-borders.py
python3 docs/validation/libreoffice-comparison/core-pagination-fix/before-after.py
```

## CIで現在の一致レベルを維持する回帰チェック

CIの旧Core比較は列幅修正前の文字位置0.1pt以内を要求していたため失敗しました。
元のPDF・AAレビュー・閾値は維持し、入力/旧PDF/修正後PDFのハッシュで限定した3件の明示的な新契約を追加しました。
新契約は文字/位置/罫線/画像/リンク/ページ寸法と144/288/300dpi画素を修正後の固定PDFと厳密比較し、既存のfont outline検証も継続します。
未知の入力に対する一括例外や閾値緩和ではありません。

さらに通常PRのLinuxジョブで、[固定基準](../../../../tests/Shared/LibreOfficeComparisonBaseline/README.md)に対して
全22入力×本体/Coreの44PDFを毎回新規生成し、ページ数・用紙・全文字と位置/色/サイズ・全描画パス・画像・リンク・144/288dpi全画素を比較します。
CIからLibreOfficeの参照生成や期待値の自動更新はしません。
文字/罫線の欠落、余分な塗り、位置ずれ、余分なページ、表示glyphを変えずに元Unicodeだけを誤らせた6変異を同じ比較処理で拒否し、罫線の独立数値監査もCIで実行します。
失敗時の実PDF・期待/実画像・JSONはCI artifactへ保存します。

ローカル実行では44件一致、6変異を検出し、旧Core 3件、既存A/B 25件、罫線変異、画像保存/描画、字体破損検出も成功しました。
実測は [ci-regression-checks.json](ci-regression-checks.json) を参照してください。
これは現在レビュー済みのLinux出力を維持する検査であり、Excel互換性やWindowsの画素一致の証明ではありません。
