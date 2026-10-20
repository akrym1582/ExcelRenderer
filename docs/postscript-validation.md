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
