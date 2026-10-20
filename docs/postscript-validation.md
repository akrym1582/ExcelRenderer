# PostScript 出力 実装・検証記録

検証日: 2026-10-20。最新 `origin/main` を取得し、計画書と同じ
`5b3e831cf973cfb3ecb1d6998a413eae30e88205` から
`feature/postscript-output` を作成した。開始時の作業ツリーはクリーン。
初回実装・検証のコミットは `11febab951c412679da6e125df10fbafbe54a854`。
当時の実装・テストファイルの SHA-256 を
[implementation.json](postscript-results/implementation.json) に記録した。

## 変更内容

- C# の直接生成による複数ページ PS。選択順とページ別用紙サイズを維持する。
- `PostScriptRenderer`、`PostScriptExportOptions`、`ConvertToPostScriptAsync`、
  `OutputFormat.PostScript`。既存 enum の番号は変えていない。
- CLI の `ps` と `render --format ps`、単一ファイル成果物、manifest、各 sink、
  ページ・範囲選択、trim、キャンセル、有界 spool に統合した。
- 全描画コマンド、罫線、図形内文字、入れ子 viewport、画像の crop/回転/反転/clip。
- 固定 glyf TrueType の文書共有 Type 42 サブセットと Encoding bank。
  TTC Face 抽出、複合グリフ closure、サブセット、権限検査は既存 Core を内部利用する。
  GID は既存 subsetter の方針どおり保持する。Core に PS 公開 API・出力責務は追加していない。
- `Drawing/CommandTextFinalizer` を PDF/PS 間で共有し、確定済み配置を保持する。
- 原文 Unicode と run/code/GID 対応の非実行コメント、独立デコーダー・検査スクリプト。
- 半透明、二値画像マスク、未対応フォント、埋め込み/サブセット禁止を明確に失敗させる診断。
- Ghostscript 必須の CI 設定、日英 README、[形式・対応範囲](postscript.md)。

## 環境と実行結果

Debian GNU/Linux 13、.NET SDK 10.0.401、.NET runtime 10.0.12、4 CPU。
Ghostscript **10.05.1**。再現用フォントは既存の同梱アセットに限定し、
公開 renderer の Fontconfig も `scripts/test-fonts.sh` で固定した。

```bash
dotnet restore ExcelRenderer.slnx --locked-mode
dotnet build ExcelRenderer.slnx -t:Rebuild --no-restore -m:1 --verbosity:minimal
source scripts/test-fonts.sh
EXCELRENDERER_REQUIRE_GHOSTSCRIPT=1 dotnet test ExcelRenderer.slnx \
  --no-build --no-restore -m:1 --logger trx --results-directory TestResults/PostScriptFinal
dotnet pack ExcelRenderer.slnx -c Debug --no-build --no-restore -m:1 \
  -p:BuildProjectReferences=false -o artifacts/postscript-packages
python3 scripts/check-no-pdfsharp.py --configuration Debug \
  --packages artifacts/postscript-packages --output artifacts/postscript-dependency-audit.json
python3 scripts/check-source-namespaces.py
python3 scripts/check-core-dependencies.py
python3 scripts/package_distribution.py
```

最終リビルドは **警告 0、エラー 0**。StyleCop の抑制は追加していない。
最終テスト集計: **2,357件成功、失敗0、skip 0**（Core 170 / Formulas 833 / Mapping 172 / OpenXml 213 / main 861 / Tool 108）。
[最終テストの集計データ](postscript-results/tests.json)も保存した。

Ghostscript は `nullpage` による全ページ実行、`png16m` による描画比較、
`txtwrite` による文字抽出で実際に起動した。5件の Ghostscript テストを含む
PS ライブラリ25件・CLI3件を追加した。実行失敗・警告・代替フォント警告・タイムアウトは
失敗扱いであり、CI の必須フラグ設定下では Ghostscript 未導入を skip しない。

[描画結果](postscript-results/drawing-comparison.json) は 300 DPI、64 pixel tile 単位の
双方向比較。欠字（日本語1文字）、5 point の文字移動、罫線削除の3変異は、
同じ閾値で失敗を検出した。
独自の幾何グリフを持つ TTC は、コピーした元ファイルの削除後、
`-dNOPLATFONTS` でシステムフォントを無効にしても描画し、直接 PNG と一致した。
IVS と補助平面の表示も同じ確定グリフからの PNG と比較した。

font subset は sfnts から独立に復元し、全体/各テーブルの checksum、
bank/code/CharStrings/GID、元文メタデータと実際の `show` の対応、DSC/showpage の
ページ数一致を検査した。[フォント検査結果](postscript-results/font-validation.json)。
ソースの注入に使えそうな括弧・逆斜線・改行等も実行命令にならず、原文を復元できる。

4パッケージを作成し、依存監査は23 project、778 managed AssemblyRef inventory、
4 package を検査して成功した。[配布物検査](postscript-results/packages.json)では
Ghostscript バイナリの同梱がなく、既存の main/tool 内の Core DLL 配布を維持している。
ライセンス変更・新規フォント配布・新規実行時依存はない。

## 文字保持の検証範囲

| 項目 | 結果 |
| --- | --- |
| 通常文字を埋め込みフォントと `show` で描く | 検証済み。全文字のパス化・ページ画像化を行わない |
| 256種類を超える日本語文字 | Encoding bank、実行、描画、標準文字の抽出を検証 |
| 原文 Unicode（IVS・補助平面・空白・改行） | バージョン付きコメントから完全復元を検証 |
| 英数字・記号・かな・標準漢字の外部抽出 | 単純な一行の txtwrite 出力を原文と完全照合。device の先頭 margin と終端改行のみ除去 |
| 折返した256種類超の文字の抽出 | device の改行・indent を除去して全文字列を照合 |
| IVS 表示 | 確定 GID の埋め込みと直接 PNG との描画比較を検証 |
| IVS 列・補助平面文字の外部完全抽出 | **Ghostscript 10.05.1 では未達**。原文メタデータ保持とは別の結果 |
| 回転・複雑な配置の外部抽出順 | 保証対象外。見た目と原文保持を別に扱う |
| PDF 変換後の任意 Unicode の完全保持 | 未検証・保証しない |

実測フラグは [extraction.json](postscript-results/extraction.json) に保存した。
独自メタデータの成功を、汎用ツールによる完全抽出成功と扱っていない。

## 性能

描画命令から直接 PS を生成する4 workload、各3回の中央値。font manager は反復間で
ウォームな場合がある。時間の単位は ms、サイズは bytes。
ピーク RSS は他の検証も含むテストプロセスの累積 high-water mark（MiB）であり、
単独変換の増分・隔離されたピークではない。絶対的な合格閾値は設けていない。

| Workload | pages | commands | 生成 ms | GS ms | PS bytes | fonts / font bytes | process peak MiB |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| japanese-borders | 1 | 320 | 27.50 | 74.83 | 915,027 | 1 / 330,880 | 1188.1 |
| repeated-text-10-pages | 10 | 3200 | 83.54 | 131.87 | 3,112,598 | 1 / 330,880 | 1188.1 |
| images-4-pages | 4 | 60 | 253.42 | 88.93 | 3,757,227 | 2 / 1,775,996 | 1188.1 |
| many-glyphs | 1 | 1 | 30.02 | 69.58 | 814,681 | 1 / 371,228 | 1188.1 |

[反復ごとの実測値](postscript-results/performance.json)には spool のピーク容量・spill 数も含む。
10ページで同じフォントをページごとに埋め込まず、文書で1フォント分を共有することを確認した。
別途 [2シート XLSX からの変換実測](postscript-results/measurements.json)も記録した。

## 制約と未実施事項

- 実プリンターによる印刷、Windows の Ghostscript 実行、GitHub Actions 上での実際の
  workflow 実行は、この Linux 環境からは未実施。CI の設定と同じ必須条件でローカル実行した。
- LanguageLevel 3 / Type 42 / PostScript 3011+ の大きな sfnt table 対応が必要。
  Ghostscript 成功を全 PS プリンターでの互換性保証とはしない。
- CFF/CFF2・可変・カラーフォント・埋め込み/サブセット禁止は診断による失敗。
  半透明や画像マスクの黙った白合成・不透明化・ラスタ fallback はない。
- 画像補間は処理系差があり、Skia の cubic sampler と完全同一ではない。
  任意の用紙寸法では、直接 PNG の ceiling と GS の rounding により既定 raster の
  寸法が1 pixel異なる場合がある。比較用 canvas の寸法は厳密照合し、
  混在用紙は別途 point 寸法・ページ数・GS raster 寸法を検証した。
- EPS、プリンター送信、印刷設定 UI、クリック可能 PS リンクは対象外。
- Ghostscript は開発/CI 専用の独立 CLI。AGPL/商用の別ライセンスであり、
  製品に組み込まず、本プロジェクトの MIT ライセンスを維持する。

## PR 作成時の main 統合

ドラフト PR #96 作成時、main は `dcac58b9d277b417b8c1f6e42920b80c319736f8`
（PR #95 の内部 input/buffer wrapper 削除）へ進んでいたため、これを取り込んだ。
PS writer は Reader の `SpillableBufferStream` を直接所有し、生成は既存の
`RenderBufferFactory.Create` を使うように変更した。バッファの共有所有権・spill metrics は
最新 main の方針に従い、wrapper を復活させていない。

上記の初回結果とファイルハッシュは履歴として保持する。統合後はリビルド警告0・エラー0、
全 **2,362件成功・失敗0・skip 0**（Core 170 / Formulas 833 / Mapping 172 /
OpenXml 213 / main 866 / Tool 108）。PS の独立文字・フォント検査、4 package の pack・
依存監査、Reader/Core の独立 restore/build/publish/AssemblyRef/実行検証も成功した。
namespace と Core 所有権の検査も成功。検証結果と変更した writer の SHA-256 は
[main-integration.json](postscript-results/main-integration.json) に記録した。

## CI: Ghostscript 10.02.1 の互換性修正

[初回CIの失敗ログ](https://github.com/akrym1582/ExcelRenderer/actions/runs/39368683848/job/118451300425)
では2,362件中、`txtwrite` を使う1件だけが失敗した。Ubuntu 24.04 の
Ghostscript 10.02.1 は正常な文字抽出後にも終了処理で
`Error: finalizing subclassing device while child refcount > 1` を出す。
同じ Ubuntu パッケージ `10.02.1~dfsg1-0ubuntu7.9` のコンテナで再現した。
製品の出力を使わない次の入力でも同じ診断が出て、終了コードは0だった。

```bash
gs -dSAFER -dBATCH -dNOPAUSE -sDEVICE=txtwrite -sOutputFile=/tmp/minimal.txt \
  -c '/Helvetica findfont 12 scalefont setfont 20 20 moveto (ABC) show showpage'
```

生成した `fonts.ps` の抽出結果は10.05.1の出力とバイト単位で一致した。
テスト用プロセス検査は、10.02.1のバナー・`txtwrite`・終了コード0の組合せに限り、
この完全一致の診断を1回だけ識別する。診断の追加文字・重複、別バージョン・device、
非0終了、構文エラー、フォントエラー・代替、その他の警告は回帰テストで失敗を確認する。
実行時の生のstdout/stderrは `PostScript/ghostscript-*.log` に保存しCI成果物に含める。
PS生成コードと文字抽出の照合・描画比較・変異検出の条件は変更していない。

修正後のリビルドは警告0・エラー0。10.02.1を使う全テストは **2,363件成功・失敗0・
skip 0**（main 867、その他は上記統合後と同じ）。10.05.1でもGhostscript検証5件と
診断判定の回帰テスト1件が成功した。両版の `fonts.ps` の抽出結果はバイト単位で一致。
原文デコーダーと `fonts.ps` / `drawing.ps` の独立フォント検査も成功した。

## PostScript レビュー指摘2件の修正

検証日: 2026-10-20。`origin/main` を再取得し、レビュー対象と同じ
`9dd0eb8a23de1c7bf0e382977f6ffd51e57705cd` から
`fix/postscript-review` を作成した。開始時の作業ツリーはクリーン。
今回の検証コードのコミットは `3ad6eafa30e3db51833fc6257ad9a7129605e387`。
過去の実測値・JSONは変更せず、今回の証拠は
[review-fixes](postscript-results/review-fixes/) に分けて保存した。

### 描画比較

輪郭の近傍一致だけを最終合格の条件にせず、`Contour`（配置・輪郭）、
`Color`（境界の色）、`Region`（均一領域の色）を独立に検査する。
すべて双方向・64 pixel tile 単位で判定する。領域色の不一致は、同じ tile 内の
大量の背景画素が一致しても相殺しない。製品側の塗り・文字・画像描画には変更を加えていない。

閾値と選定理由:

| 項目 | 条件 | 理由 |
| --- | --- | --- |
| 背景 | 白との差が各 channel 2以下だけ除外 | 8-bit量子化の差を明示的に許容。254対255は正常、250対255は欠落として検出 |
| 輪郭 | 白との差が20超、半径4 pixelの近傍 | 既存fixtureの文字位置丸め・ヒンティング差を吸収。中間グレーは除外しない |
| 均一領域 | 3x3近傍が各channel 2以内、照合色差は各channel 3以内 | ベタ色をAA境界と分離。白近傍でも内容があれば検査し、濃色同士の無条件一致を廃止 |
| 境界色 | RGB最大channel差24以内、または正規化したink色の差0.1以内 | AAによる濃度差と色相差を分ける。正規化は `(255-RGB)/max(255-RGB)`。均一領域には適用しない |
| 局所失敗 | 輪郭・境界色は15画素超かつ局所supportの12%超。均一領域は15画素超 | 輪郭の既存budgetを維持しつつ、ベタ領域の欠落を背景の一致率で薄めない |

境界色のsupportは、tile内の境界画素数と輪郭画素数の大きい方。
ベタ領域を含む局所内容に対する境界差の割合を使う。
最初のRGB距離だけの試行（最大channel差40）では、正常fixtureの境界10,204画素が
不一致となり39 tileが失敗した。均一領域は不一致0だった。
AA濃度差を色相から分離し、境界supportを内容全体に合わせた最終条件では、
正常fixtureの輪郭不一致13画素、境界色不一致759画素、均一領域不一致0画素で
失敗tileはすべて0。独自TTCでは3指標とも不一致0。
異常例固有の除外条件は追加していない。

決定的な画像では、グレー180の矩形削除・追加、濃い赤120/0/0から黒、
および120/0/0から0/36/0への置換を同じ合格条件で失敗として検出した。
最後の2色はRec.709の輝度が約25.51と25.75で、輝度だけでは識別しにくい例。
白同士、250グレー同士、境界の2 pixel移動とAA濃度差は正常として通る。
250グレー全体の白への置換、245グレー背景上の9x9 pixelの180グレー欠落も失敗する。
実際のPSを生成してGSで画像化する経路でも、灰色削除・追加と濃い赤の黒置換を検出。
既存の日本語1文字削除、5 point移動、罫線削除も検出した。
基準PNG、PSのraster、差分PNG、各指標のJSONを保存した。

### ヘアラインと破線

`BorderSide.Width` はモデルで0を禁止しておらず、既存PNG/PDF側は値をSkiaの
`StrokeWidth` と破線間隔へ渡す方式。共有モデルやSkia実装は変更していない。
PSでは幅0の実線・破線をヘアラインとして受け付ける。
`0 setlinewidth` 自体は有効であり、従来不正だったのは全要素0のdash配列。
幅のフォーマット結果が `0` の場合だけ、間隔計算の基準を固定の **1 point** にする。
これは正の微小幅（1e-18 point）が既存formatterで0になる場合も含む。
線幅は0のままで、DPIによるPSの変更やformatter全体の変更はない。
通常の正の幅は従来どおり幅の倍数を使用する。
負数・NaN・Infinityは収集時に拒否し、出力先が0 byteのまま停止する。

公開`PostScriptRenderer`から、幅0 / 1e-18 / 0.5 / 1 pointについて、
実線・Dotted・Dashed・DashDot・DashDotDotをそれぞれ線と四辺の罫線で生成した。
すべてGS nullpage実行が成功。300 DPI rasterでも線の存在、実線の連続、破線の隙間を
各線種・線/罫線ごとに確認した。PSの文字列照合だけでは合格にしていない。

### 実行と結果

Debian 13、.NET SDK 10.0.401 / runtime 10.0.12、Ghostscript **10.05.1**。
Linuxの全test呼出しで同じshellから`source scripts/test-fonts.sh`を実行した。
旧コードへ最終版の再現テストを適用した結果は **23件中12件失敗、11件成功、skip 0**。
見逃し10件と幅0/極小幅のGS setdashエラー2件を確認し、その後コードを戻して検証した。
[旧実装のログ](postscript-results/review-fixes/before.log)とテスト結果を保持した。

```bash
git fetch origin main
git switch -c fix/postscript-review origin/main
dotnet restore ExcelRenderer.slnx --locked-mode
dotnet build ExcelRenderer.slnx -t:Rebuild --no-restore -m:1 --verbosity:minimal
source scripts/test-fonts.sh
EXCELRENDERER_REQUIRE_GHOSTSCRIPT=1 dotnet test ExcelRenderer.slnx \
  --no-build --no-restore -m:1 --logger trx --results-directory TestResults/PostScriptReview/Full
python3 scripts/check-source-namespaces.py
python3 scripts/check-core-dependencies.py
python3 scripts/check-no-pdfsharp.py --configuration Debug \
  --output artifacts/postscript-review-dependency-audit.json
python3 scripts/decode-postscript-text.py \
  tests/ExcelRenderer.Tests/bin/Debug/net10.0/SampleOutputs/PostScript/fonts.ps \
  --output artifacts/postscript-review-text.json
python3 scripts/validate-postscript.py \
  tests/ExcelRenderer.Tests/bin/Debug/net10.0/SampleOutputs/PostScript/*.ps \
  --output artifacts/postscript-review-font-validation.json
```

最終リビルドは **警告0・エラー0**。StyleCop抑制は追加していない。
最終全体テストは **2,386件成功・失敗0・skip 0**
（Core 170 / Formulas 833 / Mapping 172 / OpenXml 213 / main 890 / Tool 108）。
PS関連52件（main 49 / CLI 3）もすべて成功。
[集計・各PSテスト結果](postscript-results/review-fixes/tests.json)、
[全体テストログ](postscript-results/review-fixes/tests.log)、
[リビルドログ](postscript-results/review-fixes/rebuild.log)を保存した。

namespace 663宣言とcompiler shim 2件、Core所有権・依存方向の検査が成功。
PDFsharp除去監査は23 restore project・714 managed AssemblyRef inventoryを検査して成功した。
独立PS検査は20ファイル・33ページ・18埋め込みfont resourceで成功。
sfnt全体/各table checksum、metadata/code/GID、実際のshow命令、DSCページ数を照合した。
原文デコーダーの結果、txtwriteの標準文字照合、Type 42と独自TTCの検証も成功した。
実行時依存・NuGet構成の追加はない。

[実装とPSのSHA-256](postscript-results/review-fixes/implementation.json)で
検証コミットと生成PSを対応づけた。保存証拠のSHA-256は
[artifact-hashes.json](postscript-results/review-fixes/artifact-hashes.json)に記録した。
`full-trx.tar.gz` は全6 projectの生のTRX、`ghostscript-logs/` は最終全体実行のGSログ。
旧コードの失敗は`before.log` / `before.trx`に分離している。

| 比較 | 合格 | 輪郭不一致 | 境界色不一致 | 均一領域不一致 |
| --- | --- | ---: | ---: | ---: |
| 正常fixture | はい | 13 | 759 | 0 |
| 独自TTC | はい | 0 | 0 | 0 |
| 日本語1文字削除 | いいえ | 5,744 | 4,282 | 2,370 |
| 5 point移動 | いいえ | 5,549 | 4,286 | 2,288 |
| 罫線削除 | いいえ | 4,278 | 4,385 | 618 |
| PSの灰色矩形削除 | いいえ | 21,164 | 1,163 | 20,008 |
| PSの濃い赤→黒 | いいえ | 0 | 1,861 | 31,146 |
| PSへの灰色矩形追加 | いいえ | 21,166 | 1,163 | 20,008 |

不一致数は双方向の合算。正常例では局所budget内、異常例では少なくとも1指標が
失敗tileを持つ。数字だけを加工せず、実画像の同じ比較処理から得た結果である。

今回実施していない検証: 実プリンター、Windows、GitHub Actions上の実行、
Ghostscript 10.02.1での再実行。既存10.02.1診断判定テストは全体テストに含む。
配布・プロジェクト・NuGet構成・実行時依存には変更がないためpackは再実施していない。
Ghostscriptは独立した開発/CIツールのまま。Type 42・原文メタデータ・直接生成、
半透明未対応、IVS/補助平面の外部抽出の既存制約は維持している。
