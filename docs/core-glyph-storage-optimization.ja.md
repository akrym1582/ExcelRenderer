# Coreのglyph保存・cluster構築の高速化

前回の本体対応を含む作業ツリーを変更前として、今回の2点を追加した。
PR #114以前の基準や、本体対応の改善率とは区別する。
[英語説明](core-glyph-storage-optimization.md)／
[検証記録](validation/core-glyph-storage-optimization/README.md)。

- Coreの通常outline経路は段落glyphと行の範囲を受け渡し、GlyphSequenceの
  private配列へ一回だけコピーする。確定行用の中間glyph配列を省き、完成layoutには
  段落の大きな配列やnative font所有者を保持させない。
- source/displayが同じ場合は文字要素の列挙を一回にし、最終cluster配列へ直接書く。
  空白の連続group、UTF-8 byte offset、サロゲート、glyph数検証を維持する。
- source/displayが異なる場合は旧2列挙方式を維持し、明示glyphの単一clusterも維持する。
  旧配列constructorも残し、借用Spanを受ける内部constructorを追加する。
  保存glyph・advanceは入力配列の変更に影響されず、native handleを保持しない。
- 共通algorithmはCore所有。本体の既存Break adapter・フォント解決・互換経路は維持する。
  cluster共通primitiveの同一文字列経路は本体からも使用されるため、本体回帰も実行する。

旧cluster構築の独立した比較コードを用い、固定seed、結合文字、ZWJ、国旗、空白、
不正UTF-16、glyph数不一致、IVS等のsource/display差を厳密比較する。
借用配列の変更後の不変性と、scope終了後のCore layout再描画も検証する。
既存の境界幅・公開layout再描画・本体/Core混在並列・出力失敗の回帰も維持する。

計測は同一入力・同一フォント・保存済みRelease runtimeを使用する。
2048文字超の一意入力でlayout cache hitと区別し、glyph/UTF-8/cluster/advanceを
含むdigestを時間・割り当て計測の外で比較する。
ビルド・テスト・描画検証と測定を重ねず、cold/warmと追加条件を分離する。
ピークRSSはprocess累積高水位として扱い、5%超過をpool中央値だけで合格にしない。
Windows未実行とLinuxでの検証結果を区別する。

## 検証結果

Debug/Release Rebuildはwarning/errorとも0、全solution 3073件成功。
Core coverageはline 84.82% / branch 74.34%、Performance 8件成功。
公開API差分なし。Core保存PDF、本体PDF/PNG/SVG、固定PDF基準、parity、依存分離も成功した。

Core wrap1000 fixtureの割り当ては約213.6→135.3 MiB（約36.6%削減）。
cache missのoutline単体計測は幅別に約42〜57%、同一sourceのcluster構築は約59%削減。
時間/RSSには超過ラウンドが残るため、全ケース5%非退行の合格とはしていない。
Windowsは未実行。前回の本体結果と別に、各ラウンドの生データを保存した。
