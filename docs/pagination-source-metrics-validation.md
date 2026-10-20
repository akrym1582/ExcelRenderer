# 保存行高・改ページ・元フォント寸法の検証

## 対象と範囲

- 作業ブランチ: `fix/pagination-source-metrics`
- 開始SHA: `2996f6c9940f9a4dcdaa041531c75e277911d911`（2026-10-20に `git fetch origin` 後の `origin/main`）。作業開始時の未コミット変更なし。
- 依頼計画のレビュー基準 `9dd0eb8a23de1c7bf0e382977f6ffd51e57705cd` より新しいmainを使用。現在のReader分離・Skia PDF構成に合わせ、Coreの所有箇所で修正した。
- 初回実装SHA: `da6ba4c32291f79056b7b0a6d9c944138739ad40`。以下の初回実行記録はこのSHAが対象。CI比較ハーネスの追補は末尾に分けて記録する。
- Linux / .NET SDK 10.0.401。Linuxのテストは同じshellで `source scripts/test-fonts.sh` を先に実行。
- 利用者の実ブックは未提供。症状の原因は確定していない。Windows Excel・Meiryoとの実機比較は未実施であり、合格扱いにしない。Meiryoファイルは追加していない。

## 実装

1. `CoreExcelReader.OpenXml.cs`: 保存 `ht` を `customHeight` に依存せず優先。なければシート既定、さらに未指定なら15pt。負値は既定へ戻す。NaN/Infinityの行高は既存Readerの有限数値検証で拒否。0はそのまま保持し、hidden/既定hiddenは別途適用する。行ごとのpx丸め、文字計測による高さ拡張、thickTop/thickBot加算はしない。旧メタデータアダプターも保存高さを保持する。
2. 同Reader: `man=1/true` の改ページだけを正値idで重複除去・ソートする。省略/0/falseは除外し、不正booleanは既存Readerのboolean方針と同じ `InvalidDataException`。idは既存の「その行/列の後」を維持。明示倍率は手動改ページを尊重し、FitToPagesは従来どおり無視する。診断は採用/除外件数と元XMLの保存倍率も残し、FitToPagesで適用されない保存値を追跡できる。
3. 本体の `ColumnWidthMetricsOptions` / `FullFontMetrics`: SourceFontを新既定とし、明示MDW → 明示元ファイル → 元のNormalファミリーの厳密解決 → 警告付き7pxの順。Legacyは従来の描画用ファミリー解決を再現する。Normalのサイズ・太さ・斜体を保持し、計測はpt×96/72で行う。

元フォントファイルは描画へ登録しない。外部IFontManagerのFamilyラベルだけを信用せず、物理フォントのファミリー名・Unicode name情報で確認する。組込みFontManagerは既存許可範囲だけを探索する。別名探索は小さなnameテーブルを保持し、候補全てのネイティブ描画書体を保持しない。TTCは共有extractor/スナップショットで指定faceを保持する。

キャッシュは変換単位で、Normal書体・設定・登録世代を区別する。選択ページの幾何プリフライトと本読取で同じ決定を共有する。Reader/CoreへOSフォント探索を持ち込まず、Core単独APIの単一供給フォント方針も変更していない。公開IFontManager、既存公開コンストラクター、プロジェクト・配布設定の変更なし。Fontsパッケージの必須化なし。

## 回帰fixtureと期待値

fixtureは生のワークシートXMLをZIPへ書き込み、生成ライブラリが `customHeight` を自動補完しないことを保証する。

| 検証 | 期待値と確認内容 |
| --- | --- |
| `SavedPaginationTests`（Core/本体それぞれ） | ht=12 + true/false/省略は12pt、ht=24 + false/省略は24pt。htなしはシート既定18pt、シート既定なしは15pt。負値・0・非有限数値・hiddenの位置も検証。 |
| 本文720pt、50行×12pt | 修正後は600pt、1ページ。旧高さ選択相当の15ptへモデルを差し替える対照は750pt、2ページ。最終行Y=624pt、高さ12pt。文字計測高さ1/1000でも同じ行位置・ページ数。 |
| 行break id=20 | man省略/false/0は1ページ、true/1は2ページ。手動時の次ページ先頭行は21。 |
| 行/列break混在 | 行の手動idは[20,30]、列は[1,3]。自動・id=0・重複・順序違いを除去し、明示100%で3×3=9ページ。FitToPages 1×1なら1ページ。 |
| `ColumnWidthMetricsTests` | 元寸法MDWを固定し、描画フォントNoto Sans Mono/Noto Sans JPを変えても列幅・倍率・ページ帯・最終行位置が不変。元ファミリー不在はどちらも警告付き7px。 |
| 元ファイル・厳密解決 | 同梱の再配布可能なNoto Sans Monoを実測し、SourceFontFile指定と厳密ファミリー解決が同じ値。描画はNoto Sans JPのまま。偽の登録Familyラベルは計測成功扱いにしない。 |
| キャッシュ・TTC | source/legacy/overrideの切替、Normalメタデータ不在時の7px互換と既知Normalのキャッシュ分離、同じ決定のプリフライト再利用と各シートの診断、collection-distinct.ttcのface 0/1/1切替で独立した実測値を確認。 |
| 設定エラー | 非有限/1未満MDW、負index、index単独、無効face、不在/不正ファイル、Legacy+元ファイルを拒否。明示MDWがあっても不正ファイル指定は拒否。 |
| CLI | pdf/image/svg/ps/renderへ4オプションを伝播。非0 faceとMDW指定を受理し、不正値は出力前に失敗。 |

### 統合境界fixture

保存行高12pt×65行（合計780pt）、既定行高15pt、5列×保存幅12、Letter用紙612×792pt、四辺余白36pt、本文540×720pt、横1ページ・縦0（自動）、残存自動break id=20、描画用代替フォントを組み合わせる。

- 元寸法として明示MDW=16を供給: 列幅144pt、合計720pt、最終倍率0.75。本文は585ptに縮小され1ページ。最終行Y=612pt、高さ9pt、下端621pt。
- 描画用Noto Sans Mono経由のLegacy対照: MDW=9、列幅81pt、合計405pt、倍率1、本文780ptで縦2ページ。
- 数値16はテスト用の明示寸法であり、Meiryo等の実測値や固定推奨値ではない。
- PDF/PNG/SVG/PostScriptすべてで同じ1ページ計画と診断を検証し、実際の各形式も出力する。PDFを独立リーダーで開き、1ページと最終行 `row 65` の存在を確認する。文字の見切れと余分なページの発生は別の診断として扱う。
- 元ファイル指定 + TTC face 1 + 描画用Noto + 選択ページありのPDF変換でも、プリフライトと本読取が一致し、PDF内の描画フォントがNotoであることを確認する。

## 既存期待値の扱い

Reader移行時の固定goldenは変更しない。`OpenXmlIntegrationTests` のモデル比較は従来の引数なしReader相当のLegacy、出力ハッシュ比較は従来の描画フォント経由Legacyを明示し、固定goldenの意味を維持する。実fixtureを生XMLで確認したところ、保存htの未フラグ行やman未指定の改ページはなかったため、これら14件の出力差は新既定MDWによるものだった。

テーマNormalをNotoへ代替していた既存テストは「計測成功」から「Calibri不在の7px警告」へ期待を変更する。画像デコード・条件付き書式・範囲診断の厳格モードテストは、検証対象へ到達できるよう明示MDW=7を供給する。元フォント不在警告自体を弱めたり、strict判定から除外したりしていない。

## 再配布可能なテストフォント

- Noto Sans JP Regular: `Version 2.004-H2;hotconv 1.0.118;makeotfexe 2.5.65603`、11pt。SHA256 `d930d5d52d15231c283089760f84584272ad5e37e14607ba0d19c798e7a9caec`。
- Noto Sans Mono Regular: `Version 2.007`、11pt。SHA256 `d9e2b23d19f8230be7146f409a52b1d23117e635e28f2e2892cf91b7382f325b`。
- 自作 `collection-distinct.ttc`: face 0/1を実測比較。SHA256 `178359481514fa996a3b4469f68c1c2b298f200245a439b2a3e4c538323741b9`。元ファイル・ライセンスは既存のものを使用し、追加・更新していない。

## 実行記録

### コマンドと結果（2026-10-20）

```bash
git fetch origin
git switch -c fix/pagination-source-metrics origin/main
dotnet restore ExcelRenderer.slnx
dotnet build ExcelRenderer.slnx -t:Rebuild --no-restore --verbosity:minimal -m:1
dotnet build ExcelRenderer.slnx -c Release -t:Rebuild --no-restore --verbosity:minimal -m:1
python3 scripts/check-no-pdfsharp.py
python3 scripts/validate-reader-isolation.py --work artifacts/pagination-source-metrics/isolation --output artifacts/pagination-source-metrics/isolation.json
source scripts/test-fonts.sh
dotnet test ExcelRenderer.slnx -m:1 --verbosity:minimal
python3 scripts/check-source-namespaces.py
git diff --check
```

- Debug/Releaseリビルド: どちらも警告0・エラー0。
- 全テスト: **2,453成功、失敗0、スキップ0**。Core 189、Formulas 833、Mapping 172、OpenXml 213、本体928、Tool 118。
- 名前空間: 669宣言と必要なcompiler shim 2件を確認。
- 依存監査: 23復元プロジェクト、714 managed AssemblyRef inventoryを確認し、削除済みPDF依存なし。
- Reader/Core分離: 独立consumerのrestore/build/publish、依存グラフ、出力DLL、AssemblyRef、別プロセスの保存値読取/PDF生成が両方成功。
- `git diff --check`: 成功。既存ReaderGolden・Baselines、フォント、プロジェクト、ロックファイルは変更なし。
- 配布設定・プロジェクト変更なしのためpack/配布再設計は対象外。

### CLI実出力の実測値

この作業環境の `artifacts/pagination-source-metrics/` に生XMLのxlsx、PDF、PNG/SVG/PostScript、manifest、font/input/outputハッシュとログを保持する。`render-evidence.sh` は同梱テストフォントと `--no-system-fonts` を使う。PNGはrenderコマンド既定の96dpiで出力する。

| 入力/設定 | 可視行高合計 | MDW | 列幅合計 | 最終倍率 | 縦ページ数 |
| --- | ---: | ---: | ---: | ---: | ---: |
| rows-before-simulation（旧選択相当、15pt） | 750pt | 7 | 315pt | 1 | 2 |
| rows-after（保存12pt） | 600pt | 7 | 315pt | 1 | 1 |
| automatic（保存12pt、自動break） | 600pt | 7 | 315pt | 1 | 1 |
| manual（同寸法、man=1） | 600pt | 7 | 315pt | 1 | 2 |
| boundary-source（明示元MDW） | 780pt | 16 | 720pt | 0.75 | 1 |
| boundary-legacy（描画Noto Sans JP） | 780pt | 8 | 360pt | 1 | 2 |
| boundary-compatibility（Calibri不在） | 780pt | 7 | 315pt | 1 | 2 |
| boundary-source-file（計測Noto Sans Mono、描画Noto Sans JP） | 780pt | 9 | 405pt | 1 | 2 |

manualの次ページ範囲は `A21:E50`。元MDW=16のPDF/PNG/SVG/PostScriptは、すべて `A1:E65`、倍率0.75、ページ帯1×1という同じmanifest計画になった。PNGの目視でも最終65行目が1ページ内に収まることを確認した。

SourceFontFileの機能対照で使ったNoto Sans Monoは、この合成ブックのCalibriを正確に復元するフォントとして提示していない。表の9px・2ページは指定されたファイルを実測した期待結果である。7px互換値も決定的な代替値であり、元書体との完全一致を意味しない。

未実施: 利用者の実ファイル、Windows Excel/Meiryoによる実測比較。原因の確定や実ファイルとの完全一致は報告しない。


## CI比較ハーネスの追補（2026-10-20）

[CI run 39422168818 / Core Linux](https://github.com/akrym1582/ExcelRenderer/actions/runs/39422168818/job/118625578859) は、189件のCoreテスト後の画像比較で `Page counts differ: 01-japanese.xlsx` となった。初回検証ではこのCI固有の画像比較を実行していなかった。同じ失敗をローカルの `python3 scripts/validate-core.py --visual-only --no-build` でも再現した。

固定入力は描画書体をNotoへ正規化しているが、Normal書体名にはCalibriが残っている。Coreは単一供給フォントのMDW=8を使い、本体参照側は新既定SourceFontでCalibri不在の7pxを使っていた。参照側へ同じ供給フォントを `SourceFontFile` として渡し、双方の列幅計測条件を揃えた。Coreの製品仕様と固定goldenは変更していない。

後続の `compare-pdf-backends.py` も、旧出力の列幅とMDW診断を固定比較するため、ReaderとRenderRequestへLegacyを明示した。`invoice` の修正前失敗もローカルで再現済み。新しい診断文を旧スキーマへ射影し、元/解決先ファミリー、実測MDW、severity、stage、位置、行列寸法、ページ計画と描画fingerprintの比較を維持する。旧版でブックに一度だけ出た成功診断は同一解決のシート別再利用分をまとめ、新設のRead/Info `SavedPageBreaks` 集計は旧スキーマに含めない。新しい診断の全文はfixtureごとの `mdw-diagnostics.json` と `conversion-diagnostics.json` に別途保存する。未知のMDW診断形式は例外として失敗させる。

固定PDF・golden画像・許容差・既存の差分レビュー一覧は変更していない。SourceFontの新既定とフォールバック警告の製品動作も維持する。

追補でのローカル確認:

- Release全solutionリビルド: 警告0・エラー0。
- `python3 scripts/validate-core.py --visual-only`: 17ページ成功。
- `python3 scripts/compare-pdf-backends.py --no-build`: 25 fixture、未達ゲート0。既存のレビュー済み差分51件を維持。
- `python3 scripts/compare-pdf-backends.py --no-generate --reference B --no-exceptions`: 25 fixture、未達ゲート0、レビュー済み差分適用0。
- 名前空間669宣言とcompiler shim 2件、23プロジェクト/714 managed AssemblyRefの依存監査、`git diff --check` 成功。

追補の生ログは作業環境の `artifacts/pagination-ci-fix/`、新しい比較診断とPDFは `artifacts/pdf-comparison/`、Core画像比較は `TestResults/Core/Validation/` に保持する。
