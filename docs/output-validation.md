# 出力回帰検証の再現手順

基準は PR #60 (`4ac289f23d7eb2550200af93371ceb08b5354edc`)。
初期HEAD `00039b62ce40387b2e43c863d5c152114077f8e6` との差分は
SkiaTextDrawingとFinalizedTextRenderingTestsの2ファイル。
基準コミット上のブランチで実装し、計測・描画の一次フォント共有と、Runsありの不要解決回避を維持した。

## セットアップと実行

.NET SDK 10を使用する。Linuxにはfontconfig、Python 3も必要。
Ubuntuの場合は `sudo apt-get install fontconfig python3`。
リポジトリのルートで、同じシェルから次を実行する。

```bash
source scripts/test-fonts.sh
sha256sum -c tests/ExcelRenderer.Tests/Fonts/SHA256SUMS
dotnet restore ExcelRenderer.slnx --locked-mode
python3 scripts/check-output-mutations.py
dotnet build ExcelRenderer.slnx -t:Rebuild --configuration Release --no-restore
dotnet test ExcelRenderer.slnx --configuration Release --no-build \
  --logger "trx;LogFileName=unit-tests.trx" --results-directory TestResults
dotnet pack src/ExcelRenderer/ExcelRenderer.csproj --configuration Release --no-build --no-restore
```

`test-fonts.sh` はTestResults配下にFontconfigの設定・キャッシュを作り、
同梱Noto Sans JPとテスト専用Noto Sans Monoだけを列挙する。
OSフォントの探索順に依存するテストは削除し、固定selectorの比較に置き換えた。
公開PngRendererのsmoke testはno-managerでgeneric family `sans-serif` を要求し、実画像のインクをassertする。
特定のシステムFamilyNameをassertしない。LinuxのCIでは同じ設定のgeneric aliasが同梱Noto Sans JPを選択する。
Windows/macOSではOSのgeneric sans-serifを使用する。
内部selectorの回帰テストはシステムフォントを使わない。

存在しないfamilyを指定すると、SkiaSharp 4.151.0のこのLinux環境では空のtypefaceが返り、
SKTypeface.Defaultも幅0・glyph 0となって白紙になることを再現した。
smoke testの要求を有効なgeneric familyに揃え、通常描画の確認を安定させた。

CIは固定フォントのハッシュ確認、locked restore、一時改変のred確認、改変復元後のRebuild、
全体テスト、artifact収集を実行する。TRXファイル名は指示された名称を使用するため、
複数テストプロジェクトの同名TRXが上書きされる場合がある。両プロジェクトの成功は
コマンドの終了コードと各プロジェクトのconsole summaryでも確認する。

## 検証範囲

| 項目 | テスト名 | 実出力でのassert | 修正／制約 |
|---|---|---|---|
| 固定Skiaフォント | `Skia_legacy_uses_selected_face_for_measurement_and_drawing` (8ケース) | 2固定フォントの幅・画像の差を先にassert。通常／縮小はtext・path両方、Bold／Italic要求、中央／右、改行／wrap。直接描いた参照画像と一致、selectorは1回 | 固定RegularのためBold／Italicは要求フラグの伝達を検証し、字形の再現とはしない |
| 確定Skia | `Skia_finalized_empty_runs_use_selected_primary_face` (2ケース)、`Skia_finalized_runs_do_not_select_an_unused_primary_face` | Runs空は保存された(10,23)/(10,47)・9ptで直接参照画像と一致。Runsありはselector呼出禁止、実インクあり | PR #60の修正を維持 |
| PDF文字 | `Pdf_finalized_text_uses_stored_run_origins_and_font_size` (3ケース) | 左上／中央中央／右下のA/B/C原点を固定数値で0.05pt以内、実サイズ9pt。run/show順で識別、通常文字はtext操作のみ | 自然な再計測結果を期待値に使わない |
| PDF glyphパス | `Pdf_finalized_glyph_path_uses_stored_origin` | 固定フォントの実在A glyph、全出力点の(18,14)pt移動と絶対アウトライン位置 | 存在しないIVSを前提にしない |
| PDF下線 | `Pdf_finalized_underline_is_drawn_once_per_line_at_stored_baseline` (6ケース)、既存`Finalized_pdf_font_does_not_enable_automatic_underline` | Runsあり／空、下線なしはpaint 0、ありは水平stroke 2本。左／中央／右で始終点・baseline+1をassert。余分なfillも検出 | 自動下線抑制テストだけで代替しない |
| SVG再描画 | `Svg_serialized_finalized_text_matches_png_geometry` (3ケース)、`Svg_serialized_legacy_text_matches_png_geometry` (3ケース)、`Svg_serialized_rotation_preserves_clip`、`Svg_serialized_finalized_underline_has_stored_position_and_count` | 実SVG bytesをSvg.Skiaへ読込、120×80pxで72dpi PNGと双方向の1px境界比較。人工文字の独立glyph検査領域・外接矩形、serialized path座標0.05pt、wrap／shrink中央・右、30度clip、下線2本の座標・画素 | 白背景・viewport尺度を一致。平均誤差や全画面インクのみでは判定しない。既存画像／カラー絵文字構造テストは維持 |
| xlsx Readerアンカー | `Reader_preserves_drawingml_anchor_coordinates`、`Reader_anchors_reach_draw_commands_with_print_origin_and_scale` (4ケース) | 保存xlsxを通常Readerで再読込。3種類のmarker／EMU／extent／editAs、A1とC4開始、scale 1/0.5、独立の列幅累積と明示行高からDrawImageCommandを0.01pt以内でassert | 検出したeditAsの`Value.ToString()`誤りを`InnerText`へ最小修正 |
| Skia例外復元 | `Skia_legacy_restores_canvas_after_drawing_failure`、`Skia_finalized_restores_canvas_after_drawing_failure` (各4ケース)、既存`Skia_restores_finalized_text_state_after_drawing_exception` | 回転0/30、clip後の描画例外、SaveCount、matrix、clip、Bounds外sentinel画素。callerの既存translate／clipも保持 | 確定は読める固定フォントの既知空space outline、互換はclip／計測後にfake managerをarmして描画ResolveTextRunsで失敗 |
| PDF例外復元 | `Pdf_finalized_restores_graphics_after_drawing_failure`、`Pdf_legacy_restores_graphics_after_drawing_failure` (各4ケース)、`Pdf_legacy_ordinary_text_preserves_caller_transform_and_clip` (2ケース) | 同じXGraphicsを続けて使用し保存。実sentinel原点・四隅と描画時active clipをassert。失敗側のclip適用も実streamで確認。callerのtranslate／clipを保持 | 互換resolved経路はclip／計測後のinternal observerでfakeをarm。通常経路のSave/Restoreも実出力で検証。既定observerはnull、public API追加なし |

PDF helperはContentReaderから演算子・operandを読み、q/Q、cm、text/line matrix、Tf、
文字show、path、clipを解釈して上端基準ptへ変換する。gsはopacityだけのresourceに限定する。
未知演算子やfont-dependentな連続文字送りは診断付きで失敗する。汎用PDF parserとしては使わない。

SVG比較が失敗すると、元SVG・PNG・SVG再描画PNG・差分PNGを
`tests/ExcelRenderer.Tests/bin/Release/net10.0/SampleOutputs/Regression/` に保存する。
既存CI artifactへ含まれる。mutationのログと結果JSONは `TestResults/Mutations/` に保存する。

## 固定依存

| 依存 | 固定方法 | ライセンス／配布 |
|---|---|---|
| Noto Sans JP | 既存third_party資産を再利用。既存noticeのSHA-256とOFLを維持 | 既存のoptional font package |
| Noto Sans Mono Regular | notofonts/noto-fonts commit `ffebf8c1ee449e544955a7e813c54f9b73848eac`、SHA-256 `d9e2b23d19f8230be7146f409a52b1d23117e635e28f2e2892cf91b7382f325b` | OFL同梱。tests以下のみ、製品packageへ追加しない。テスト中downloadなし |
| Svg.Skia | 公式NuGet 5.2.3。package metadataはnet10.0とSkiaSharp >=4.148.0を宣言。既存4.151.0とrestore/build/実再描画で確認。test projectのpackages.lock.jsonにtransitive version/content hashも固定、CIはlocked-mode | MIT、PrivateAssets=all、製品依存に追加しない。source commit `7910666415a96a09643d1729eb5da5d115d75748` |

## 一時改変の検出確認

`check-output-mutations.py` は各改変について対象テストだけを実行し、
**テストのfailure summary**を確認する。build errorや未検出をred成功には数えない。
各ケースのfinallyと全体finallyで元ファイルのbytesを戻し、SHA-256と復元結果をJSONに記録する。
最後のRebuildと全体テストは必ず改変復元後に実行する。

| 一時改変 | 対象 | 必須結果 |
|---|---|---|
| Skia描画だけMonoへ変更 | 固定フォント画像比較 | red |
| PDF Run.X無視 | 3配置のrun原点 | red |
| PDF BaselineをY+9へ変更 | 3配置のbaseline | red |
| PDF下線を重複描画 | 本数と位置 | red |
| SVG path側Run.X無視 | serialized SVG→rasterと人工文字領域 | red |
| Reader EMU半分／marker +1省略 | 保存xlsx→Reader | 各red |
| Skia互換／確定の内側Restore省略 | 各sentinel | 各red |
| Skia外側回転Restore省略 | 互換・確定sentinel | red |
| PDF互換resolved／通常／確定の内側Restore省略 | 各sentinel | 各red |
| PDF外側回転Restore省略 | 互換・確定sentinel | red |

実行結果とCI状態はこのブランチのPRおよびartifactに記録する。

## ローカル実行結果

.NET SDK 10.0.401 / Linux x64でlocked restore成功、Release Rebuildはwarnings 0 / errors 0。
全体テストはExcelRenderer.Tests 259/259、ExcelRenderer.Tool.Tests 24/24成功、Skip 0。
ExcelRenderer 1.6.7のpackも成功し、SVG rasterizerとMono fontが製品nupkgに入らないことを確認した。

| 改変 | 検出した失敗数 / 対象ケース | 結果 |
|---|---:|---|
| `skia-drawing-face` | 8 / 8 | red確認済み |
| `pdf-run-x` | 3 / 3 | red確認済み |
| `pdf-baseline` | 3 / 3 | red確認済み |
| `pdf-double-underline` | 4 / 6 | red確認済み |
| `svg-run-x` | 3 / 3 | red確認済み |
| `reader-emu` | 1 / 1 | red確認済み |
| `reader-marker` | 1 / 1 | red確認済み |
| `skia-legacy-restore` | 4 / 4 | red確認済み |
| `skia-finalized-restore` | 4 / 4 | red確認済み |
| `skia-rotation-restore` | 4 / 8 | red確認済み |
| `pdf-finalized-restore` | 2 / 4 | red確認済み |
| `pdf-legacy-resolved-restore` | 2 / 4 | red確認済み |
| `pdf-legacy-ordinary-restore` | 1 / 2 | red確認済み |
| `pdf-rotation-restore` | 4 / 8 | red確認済み |

PDFの内側Restore省略は回転0度でred、30度では外側Restore(state)も内側stateを復元するためgreen。
外側Restore省略は30度でred。両方を個別に変異させ、互いによる復元の隠蔽も確認した。
全14改変の終了後に元のbytesを確認し、改変なしで全体Rebuild／テストを実行した。
