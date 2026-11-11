# 本体の描画高速化

PR #114マージ後の `a71e21d9f8a612c8747918383eced1742b385ada` を変更前とする。
[英語の実装説明](main-performance-optimization.md)と
[検証・測定記録](validation/main-performance-optimization/README.md)を参照。

## 実装した範囲

- PDFは文書writer、PNG/SVGは描画呼び出しscopeがdispose可能なdrawing contextを所有する。
  CoreのPaint所有者を共有し、fill/stroke各一個と最後のdash effect最大一個を遅延生成する。
  部分書式の色、行の基本色、emojiの白色と復元を明示する。図形・画像のcanvasも例外時に復元する。
  SVG白背景とnative互換計測の既存Paint方針は維持する。
- CLOCK退避の所有者をCoreへ共通化する。本体のlayout/width/line/rich各cacheを
  512件・保持量131072単位で制限し、2048文字超は保存しない。
  text/source/行/run/UTF-8/glyph/cluster/advanceとキーのfamily名を計上する。
  richキーは空runの件数も数え、色・下線等を含むFontを維持する。
  文字数単位はheap/RSSの実byte上限ではなく、既存の共有font snapshot保管全体の上限でもない。
  通常文字のキーはfamily/size/bold/italic/登録世代を保持し、未使用の幅と装飾を除く。
  登録世代が変わると古い値を解放する。standalone通常文字の強参照cacheは追加せず、
  rich値の弱参照・8キー・instance lockを維持し、キー保持量を16384単位で制限する。
- 通常outline段落のglyph取得・文字要素折り返しをCoreのOutlineParagraphへ抽出する。
  本体では内蔵FontManager、単一通常run、非color、正常UTF-16、source/display一致の
  場合だけ適用する。確定行は同じglyphを受け取り、UTF-8/clusterを行基準で再構成する。
  互換/rich行の計測済みglyphもfinalizationへ渡す。
  既存TextRunBuilderへ文字要素metadataを直接渡し、merge前の一時run生成を省く。
  merge条件・UTF-16開始位置・特殊glyphのflagは維持する。
  複数font、IVS/emoji等の特殊glyph、置換、不正UTF-16、カスタムmanagerは互換経路を維持する。
  rich折り返しとPNG/SVGのnative float計測は変更しない。
- PDFのtrim/range/追加診断/全command observerなしのpreflightを条件付きで軽量化する。
  既存の選択済み候補に文字セルがあり、候補にCF装飾がないページだけが対象。
  空セル・結合罫線・画像配置・罫線neighbor等を省きながら、本体hookを呼んで
  source regions・clip・図形metadataを維持する。
  共通セル文字→Z順の図形文字→共通header/footerの順に収集する。
  読取診断・リンク解決の時点と二周構成・active payload最大一個を維持する。

## 既存対策と適用外

通常ページ候補の引き継ぎ、PDF positionのnative Span直接書き込み、確定glyphの直接収集、
完成済みcommandの再利用は変更前から存在するため、新規改善として数えない。

複数fontの線形折り返しは未適用。runごとのdouble換算・加算順序と行分割の同値性を
別に証明する必要がある。rich折り返し、PNG/SVG互換折り返し、FontManager内部cacheの
方針も従来どおり。trim/range/診断/CF装飾/連続出力/PSには完全preflightを残す。
公開API・TFM・依存版・配布構成・Core/Reader境界を維持し、公開layoutへnative handleを追加しない。

## 検証の区分

固定seedと境界幅の直前/一致/直後で、旧候補計測と行文字・幅・metrics・run・glyph・
UTF-8/clusterを厳密比較する。cache境界、保持量、font登録後無効化、装飾、Paint pixel、
preflight適用条件も検証する。既存のscope終了後再描画・並列・入れ子・例外・キャンセル・
非seekable/同期I/O禁止出力の回帰を併用する。

保存済みRelease runtime・同一入力bytesで、ビルド/テストを重ねず交互順序のラウンドを測る。
cold/warmとobserver付き診断を分け、包含する工程時間を足し合わせない。
ピークRSSはprocessの累積高水位であり、各試行でリセットされた値とは扱わない。
時間/RSSの5%超過をpool中央値だけで合格にしない。過去のCore改善率は本体へ流用しない。
Windowsの実行結果はLinuxと区別して報告する。

## 実行結果と未達条件

Debug/Release Rebuildはwarning/errorとも0、全solutionは3061件成功。
Coreはcoverage対象443件、line 84.79% / branch 74.40%、Performance 8件成功。
本体2361項目・Core 82項目の公開APIは差分なし。保存PDF/PNG/SVG、Core 7 case、
既存PDF 25 fixture、main/Core 19比較、画像検証が成功した。

主比較の短幅長文workbookは各ラウンド9.7〜22.3%短縮、割り当て約11%減。
別に追加した幅255・20セルの通常文字長文fixtureは各ラウンド94.9〜97.0%短縮、
割り当て3687.76→54.34 MiB、layout GetGlyphs 120120→40回だった。
どちらも当該fixture・プロトコルでの複合変更の実測であり、一般的な改善率ではない。
追加PDFもpixel・文字/glyph座標・罫線・寸法・埋め込みsubsetが一致した。

**時間/RSSの全ケース5%非退行条件は、記録した試行では未達。**
主比較で超過した全caseをtiering無効・warm追加・8試行・3ラウンドで再調査したが、
超過と方向の変動が残る。特に通常text1000-Pdfの主比較は8.4〜21.2%遅く、
追加比較の時間比は1.090 / 0.983 / 1.080。短幅長文の追加比較も
1.034 / 1.017 / 1.037だった。ノイズだけが原因と断定せず、未達・未解決として残す。
Windowsは未実行。全case・ラウンド別の生データを検証記録に保存した。

4配布packageのローカルpack、11独立consumer、font package分離の検証も成功した。外部への公開は行っていない。
