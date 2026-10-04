# 範囲・trim・リンクの実装と検証記録

2026-10-04。基準／着手時の最新 origin/main は `f3efa99e5aa31b7c3e5248fc901abba721dd420e`。作業ブランチは `feat/ranges-trim-hyperlinks`。依存とパッケージ版数は変更していない。

## 変更と実出力の証拠

以下のテスト名は、特記がない限り `RangeTrimHyperlinkTests` 内にある。fixture は ClosedXML で保存し、Open XML SDK でリンク／描画 XML を補完した後、通常の ExcelReader で再読込している。

| 機能／変更対象 | 対応テスト | 確認結果・修正した不具合 | 一時改変 red |
|---|---|---|---|
| 範囲 parser／SelectionOptions／CLI validation | `Parser_accepts_only_normalized_A1_rectangles`, `Parser_rejects_non_rectangles`, ToolIntegrationTests の範囲・引数テスト | 引用符、絶対参照、上限、重複、同一パス、既存 PDF 未truncate を確認 | 該当なし |
| 元セル geometry／本文・反復タイトル領域／結合 clip | `Generated_range_replaces_print_area_and_preserves_partial_merge_geometry`, `Explicit_geometry_and_body_mapping_apply_scale_exactly_once`, `Existing_three_anchor_saved_fixture_is_clipped_without_canvas_expansion` | PrintArea 置換、範囲外の結合左上、倍率 0.5／1、3種 anchor の保存再読込。罫線のない結合も保持するよう修正 | 範囲外結合左上を捨てる改変を検出 |
| viewport／描画境界／PDF・PNG・SVG descriptor | `Artificial_scene_trim_and_PDF_annotations_have_independent_numeric_expectations`, `Generated_trim_uses_final_dimensions_in_actual_outputs`, `Samples_are_saved_and_serialized_SVG_matches_trimmed_PNG` | 独立期待値104×64pt、PNG139×86px、描画原点、PDF保存再読込、SVG保存再読込とbitmap比較。罫線・白塗り・calloutを境界へ含める | trim原点を移動しない改変を検出 |
| 空・非表示・資源・caller状態 | `Hidden_only_selection_preserves_empty_trimmed_and_continuous_pages`, `Paginated_PNG_pixel_limit_fails_before_open`, `Viewport_restores_caller_canvas_after_drawing_failure` | 空ページ維持、padding0／2、bitmap確保前上限、例外時transform／clip復元 | 既存renderer状態復元の14改変も検出 |
| SDKリンク／名前／literal数式 | `Saved_xlsx_reader_keeps_xml_ranges_empty_links_and_literal_displays`, `Literal_formula_scanner_consumes_the_whole_expression`, `Saved_scoped_names_and_overlapping_definitions_are_deferred_until_output`, `Link_only_ranges_do_not_materialize_blank_cells_and_keep_output_links`, `Readable_invalid_ref_is_deferred_but_missing_relationship_is_fatal_before_open` | 範囲ref、空セル、表示・cached値分離、スコープ優先、重複拒否。存在しない修飾シートの名前をリンク元の同名定義へ誤解決しないよう修正。不正ref `#REF` は遅延Warning、ClosedXMLが拒否する壊れたrelationshipは致命的エラー。リンク用に合成された空セルをReportCellへ大量展開する問題を修正 | 該当なし |
| URI方針・diagnostics・Strict | `Uri_policy_does_not_double_encode_or_accept_unsafe_targets`, `Strict_and_invalid_arguments_do_not_open_the_sink`, `Suppressed_hyperlink_failures_still_fail_before_open_and_excluded_sources_are_quiet`, `Continuous_range_has_fixed_dimensions_and_unsafe_link_diagnostics_are_not_evaluated_for_images` | mailto CRLFのpercent encodingも拒否、None／画像はリンク診断なし、抑制と失敗判定分離、出力除外元は無通知 | 該当なし |
| 最終PDF注釈 | `Artificial_scene_trim_and_PDF_annotations_have_independent_numeric_expectations`, `Generated_PDF_destination_references_final_selected_document_page` | 保存PdfReaderでRect／URI／Dest／XYZ／最終Pages参照を確認。四数値PdfRectangle ctorを使わずXPoint組を使用。選択前ページ4→出力2 | PDF Y未反転、選択前ページ番号を使用する改変を検出 |
| Markdown全formatter経路 | `Markdown_links_anchors_lists_and_HTML_attributes_are_safe_and_None_is_plain`, `Saved_scoped_names_and_overlapping_definitions_are_deferred_until_output` | HTML href/title属性escape、ASCIIアンカー、空／範囲リンク一覧、式説明をリンク外へ配置、None。リンク先アンカーを対象セルの出力文脈へ配置 | HTML href未escapeを検出 |
| API／CLI／互換文書 | ToolIntegrationTests、既存全出力回帰 | 旧PDF／Markdown APIも共有リンク経路。日英README、architecture、CLI help更新。既存テストのSkipなし | 既存14改変を維持 |

## 最終コマンド

Linux、.NET SDK 10.0.401。指定フォントの SHA256 は全件一致。locked restore、Release Rebuild（警告0／エラー0）、全テスト、library／tool pack に成功。全テストはライブラリ318件、CLI30件、失敗0／Skip0。

`python3 scripts/check-output-mutations.py` は19件すべてテスト失敗を検出し、`TestResults/Mutations/results.json` の `sources_restored` は `true`。検出後の最終Rebuild／testはgreen。Destのテストは、誤ったページ番号がPDFsharpによって最終ページへclampされて偶然正解になるのを避け、出力ページを3ページにして検出力を確認した。

CLI nupkgをローカルpackage sourceから `/tmp/excelrenderer-packaged-final` にインストールし、`--no-system-fonts` でPDF、連続PNG、連続SVG、Markdownを生成した。別プロセスのprobeで保存PDF寸法と注釈、PNGdecodeとmanifest、SVG再読込とPNG比較、Markdownアンカーの一意性／参照整合を検査した。CLI標準エラーに出るfixture由来のWarningも想定どおり。

## 取得先

リポジトリルートからの相対パス。これらは生成物のためgit管理外で、`Samples_are_saved_and_serialized_SVG_matches_trimmed_PNG` を実行すると通常APIのサンプルを再生成できる。

- `TestResults/RangeTrim/fixture.xlsx`
- `TestResults/RangeTrim/sample.pdf`, `sample.png`, `sample.svg`, `sample.md`
- `TestResults/RangeTrim/packaged/report.pdf`
- `TestResults/RangeTrim/packaged/png/Sheet1.png`
- `TestResults/RangeTrim/packaged/svg/Sheet1.svg`
- `TestResults/RangeTrim/packaged/markdown/workbook.md`
- `TestResults/RangeTrim/packaged/*json`（manifest）

PNGおよびPDFをraster化したサンプルを画像として目視確認した。Excel実機比較、Windows／macOSでの実行は行っていない。

## 公開状況・未実施項目

ブランチをpushし、GitHubコネクターで [Draft PR #62](https://github.com/akrym1582/ExcelRenderer/pull/62) を作成した。gh経由のAPIアクセスは拒否されたがコネクター経由で完了した。PR用本文は `docs/ranges-trim-hyperlinks-pr.md` に保存している。すべての必須ケースの全組合せを個別テスト化したわけではない（例：回転文字のtrimと内部リンクの同時指定、複数PrintAreasと反復タイトルを同時に持つ全倍率のリンク矩形、全rendererに対するキャンセル時の追加fixture）。既存回帰に加え上記の独立数値・保存再読込・mutationを実施した範囲を証拠として扱う。
