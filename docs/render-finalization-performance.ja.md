# 描画コマンド確定と性能改善

罫線修正 `38390a6` と、そこで取り込み済みのmain `513ddd4` を基準にしています。
元の調査 `2996f6c` より後のフォントメトリクス・罫線・rich text・PS対応を維持します。
[English](render-finalization-performance.md)。

本体のPDF/PS/PNG/SVGはすべて `CommandTextFinalizer` を経由してから、コマンド診断・
trim境界・フォント収集・描画を行います。既存layoutの改行・寸法・baseline・run位置・
個別書式を変えず、不足するrun/glyphだけを補います。図形の既存 `TextCommand`、
確定済みの枝とページリストは参照を再利用し、Viewportは再帰、連続出力は逐次列挙を保ちます。

PNG/SVGの互換入力には `SkiaCompatibilityTextLayout` を使い、従来のnative計測・縮小・
float丸めを確定情報へ保存します。PDFのメトリクスを適用して画像を変えることはしません。
公開layoutはフォントのimmutable snapshotとglyphを保持し、native handleを含みません。
独立したRenderer呼び出しはフォント・画像scopeを確保し、終了時に外側へ復元します。
入れ子の公開Raster呼び出しは同じresolverインスタンスのときだけサービスを再利用し、
呼び出し側が明示したフォント方針を尊重します。

PDF/PSのpreflightで選択ページを構築・確定し、診断と境界・descriptorを決定してから
同じコマンドのフォントを収集します。コマンドを解放し、リンクをmetadataで解決後、
描画時に一度再構築するため、通常は **選択ページあたり2回の構築** です。
全ページのコマンドをcacheしません。時刻入りヘッダーは同じ時計snapshotを使います。
前倒し生成したwriterも失敗・cancel時にDisposeされます。新たにpreflightで検出するPS
エラーではsinkを開きません。font準備・書き込み失敗は既存のAbort契約を維持します。

PDFの収集はCoreの `PdfFontCollection` へ直接glyphを登録します。従来と同様に透明文字も
収集し、Null Surface・配置・Paint・画像decode・図形pathは作りません。PSは透明度等の
形式検査を維持し、収集時の文字配置を省きます。glyph・原文・clusterの配列は外部から
変更できず、コピーするnative APIへ内部read-only spanを貸します。描画位置とnativeの
文字bufferは独立した領域が必要なため残します。

CFは元sqrefと式anchorを保持したまま、隣接する表示判定用範囲を結合し、有限の本文・
タイトル範囲との交差を同じwork上限下で逐次生成し、タイトル行×列の直積を配列化せず、
行帯・列区間へ正規化して各座標を一度列挙します。優先度順のrule
配列を帯ごとに共有し、セルの結果は個別評価します。priority、stopIfTrue、属性の予約、
結合外周と範囲外の辺の契約は維持します。合成辞書は必要なときだけ作り、動的fillだけなら
空の読み取り専用ビューを使います。数式target辞書はRecalculate時だけ、外部delegateが
ある場合は不要なhidden/merge索引を作りません。CF無効では式・書式準備と表示索引を省き、
CFなし・無効・Coreのsnapshot契約は維持します。

表示候補は従来のユニーク200万座標に加え、正規化work 1600万単位、保持区間・rule参照
200万件の別上限を持ちます。候補・rule計画を適用前に完成させ、正規化の上限超過で一部の
書式だけを適用しません。sortの入力も有界です。work単位は区間・event・参照の検査数で、
CPU命令数ではありません。上限時の警告は先頭が非表示でも表示対象を最大200万座標の
有界・行単位cancel付き探索で見つけ、候補書式は作りません。母集団は非表示・選択外を含む元範囲全体で、既存の別上限を保ちます。

trimは確定glyphと配置からink境界を計算します。変換単位のidentity cacheは最大512件、
各2048glyph・原文16384byteまでとし、face取得より前に確認します。native handleは残しません。
回転・clip・明示IVS path・emojiの保守的境界・下線の外側処理を維持します。実際の不整合が
一つあり、通常フォントの下線が補完済みrunにある場合も、rich下線と同様にtrimへ含めるよう
修正しました。baselineから独立に期待境界を計算するテストを追加し、凍結契約は変更しません。

PS画像のalpha分類はCoreの変換単位の画像所有者で最大512件、byte配列の弱い参照identityで
保持します。decode画像の退避後も再利用し、opaque metadataなら走査せず、走査は行ごとに
cancel確認します。余分なbitmapは保持しません。上限時にcacheを空にすると後で再走査する
場合はあります。本体の `ShapeGeometry` で基本形状を共有し、Skiaのnative楕円・角丸とPSの
既存cubic近似は維持します。pathは使用scopeで解放し、Coreへ高度機能を追加しません。

[検証結果](validation/render-finalization-performance/README.md)にDLL・入力hash、診断別実行、
交互測定のcold/warm値、品質と制約、再現コマンドを記録します。工程時間は包含関係があるため
単純加算できません。構築回数は逐次列挙の完了を含む実処理eventを数えます。
パッケージ・バージョン・配布構成は変更しません。
