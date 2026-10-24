# Microsoft Excel実出力との比較 / Windows Excel reference validation

この検証は **Windowsデスクトップ版Microsoft Excelの実出力** と、本体版・Core版を比較します。
初期状態の `manifest.json` は空です。22ケース・44比較は **未検証 (unverified)** であり、
Excelとの表示一致を確認したという意味ではありません。既存のWindows Excel正解PDF一式の所在は未確認です。
LibreOffice参照PDFや製品自身のPDFをコピーして正解にする手順はありません。

The existing 44 product regression baselines, their hashes and CI gate remain unchanged.
This separate workflow reuses the same 22 XLSX files and the extraction alignment,
text inventory and rasterization helpers in `scripts/compare-libreoffice.py`.
Only XLSX identities are read from the provisional Calc catalog; no Calc PDF is
required, rendered, imported or used as an Excel oracle.

## Windows Excelで必要な作業

1. このPRをWindowsの作業用ディレクトリへチェックアウトします。ライセンス認証済みの
   デスクトップ版Microsoft Excelと、PDF出力が利用できるWindowsユーザーセッションが必要です。
   GitHubの通常の `windows-latest` にExcelがあるとは仮定しません。
2. 下表の要求フォントを確認します。`Noto Sans JP` はリポジトリの
   `third_party/NotoSansJP/NotoSansJP-Regular.ttf` をWindowsにインストールします。
   Calibri、游ゴシック、MS Gothic等の有無も確認し、不足はPDF登録前に記録・解消してください。
   比較側のLinuxは固定フォント（Noto Sans JP / IPAmjMincho / Noto Sans Mono）を使用します。
   したがってWindowsとフォントが同一とは限りません。フォント変更で元XLSXを書き換えないでください。
3. Excelで作業中のファイルを保存して閉じます。PowerShellでリポジトリのルートから以下を実行します。
   スクリプトは新しいExcel COMインスタンスを起動します。既存のExcelプロセスを終了させる処理はありません。

   ```powershell
   powershell -NoProfile -File scripts/export-excel-reference.ps1 `
     -OutputDirectory C:\ExcelEvidence\export-20261024 `
     -Operator "reviewer-id"
   ```

   一部だけ出力する場合は `-CaseId 01-japanese,02-image` を指定します。
   出力先は新規ディレクトリが必須です。各ケースに `<id>.pdf` と `<id>.json` が生成されます。
4. PDFを開き、対象の全可視シート、印刷範囲、用紙方向・サイズ、改ページ、図形・画像が
   期待どおり含まれていることを確認します。警告・修復・外部リンク・フォント置換が発生した場合は
   原因を解決して新規ディレクトリへ再出力してください。出力したPDFを編集・最適化しないでください。
5. 出力フォルダを比較用チェックアウトへコピーし、下記の登録コマンドを実行します。
   登録されたPDF・証跡JSON・manifestの差分をレビューして同時にコミットしてください。
   Windowsのフォントファイル自体はアップロードしません。証跡にはファイル名とSHA256だけを含めます。

### Excelの出力設定

スクリプトは更新リンク無効、マクロ無効、読取専用で元XLSXを開き、計算モードを手動にします。
明示的な再計算・保存は行いません。本体の `cached` とCoreの保存済み値読取に対応する条件です。
Excelが内部的に値を更新する可能性まで否定するものではなく、値の差は別途調査してください。
`Workbook.ExportAsFixedFormat` のPDF/標準品質で、既存印刷範囲を尊重し、ページ範囲を指定せず、
全可視シートをブック順に出力します。元ファイルのハッシュを出力前後に検証します。

画面で確認する際の対応操作は、Excelの **ファイル → エクスポート → PDF/XPSの作成**、
**最適化: 標準**、**オプション → 発行対象: ブック全体**、**印刷範囲を無視する: オフ** です。
ページ設定や拡大縮小率はXLSXに保存された設定を変更しません。
正式登録用は上記スクリプトで出力し、手作業で証跡を捏造しないでください。

Excelバージョン/build、Windowsバージョン、プリンター、UTC出力日時、操作者、全インストール
フォントのハッシュ、入力/PDFハッシュ、シート・計算・出力条件を記録します。
PDF metadataのMicrosoft Excel識別も照合し、LibreOffice/Skia/PDFsharp等を拒否します。
**これらは出所をレビューする証跡であり、暗号学的なExcel実行証明ではありません。**
偽造metadataを技術的に完全排除できるとは主張しません。出所不明のPDFは登録しないでください。

## Register and run

From the repository root (Python 3.10+; CI uses 3.12):

```sh
python -m pip install -r scripts/excel-reference-requirements.txt
python scripts/excel_reference.py register /path/to/windows-export
# Explicit replacement only after reviewing a new Excel export:
# python scripts/excel_reference.py register /path/to/new-export --replace

dotnet restore src/ExcelRenderer.Tool --locked-mode
dotnet build src/ExcelRenderer.Tool -c Release --no-restore
source scripts/test-fonts.sh  # Linux: isolate system font discovery
python scripts/excel_reference.py run --output artifacts/excel-run-001 --fail-on-difference
```

Registration accepts a subset, validates every supplied case before writing, and
stores content-addressed PDFs/evidence plus their hashes. Existing entries require
`--replace`; old content-addressed files remain for review. It never changes the
source XLSX, Calc reference, or product regression baseline. A registered but
missing, modified or invalid PDF/evidence is an **error**, not unverified.
Paths cannot escape the selected reference directory. A different reference root
can be supplied with `--reference /path/to/registry` (including its manifest).

Use a fresh output directory when generating. `--no-generate` compares existing
`actual/{main,core}/<id>.pdf` files with their `.run.json` provenance, including
input/output hashes and selected sheets. Copying an unrelated PDF into `actual/`
without matching generation evidence is an error for a registered reference.
The comparator does not generate Excel PDFs. A run with no registered references
still renders all 44 product PDFs, while reporting every Excel comparison unverified.

```sh
# Enforce complete Excel coverage in addition to differences:
python scripts/excel_reference.py run --output artifacts/excel-run-002 \
  --require-all-references --fail-on-difference
# Repeat analysis of a completed run (does not regenerate PDFs):
python scripts/excel_reference.py run --output artifacts/excel-run-001 --no-generate
# Tooling tests use synthetic PDFs, never registered repository golden files:
python -m unittest discover -s scripts/tests -p test_excel_reference.py -v
```

Exit codes: 0 means the requested execution policy completed, **not** necessarily
Excel fidelity. 1 means generation/reference error, or a policy-enabled visible
difference/missing reference. 2 means invalid command/configuration/registry.
`--fail-on-difference` fails on `review-required`; `--require-all-references` fails
on unverified coverage. CI enables the former; the latter is an optional manual
workflow input until actual Excel references are registered. No thresholds or
expected Core differences silently exempt a case.

## Evidence and interpretation

- `report.json`: all 44 pair statuses, reference/actual hashes, page counts,
  sizes/MediaBox/CropBox/rotation, matched character origins and bounding boxes,
  decoded strokes/paths/images/fonts, and raster measurements.
- `junit.xml`: unregistered cases are **skipped**, visible differences are failures,
  corrupt/missing registered evidence or actual output is an error.
- `summary.md`: coverage and `fidelity_passed` explicitly false for any unverified
  pair. The independent `Microsoft Excel reference comparison` workflow publishes
  it to the Actions summary and uploads the entire output directory.
- `environment.json`, `actual/**/*.run.json`, main renderer manifests: runtime and
  font hashes, commit, OS, command lines, stdout/stderr and renderer diagnostics.
- `comparisons/<engine>/<id>/*inspection.json`: decoded component coordinates;
  each page at **144 and 288 dpi** has `excel.png`, `actual.png`, `difference.png`
  and `overlay.png`. Extra/missing pages are retained against an empty white canvas.

Pages are paired by ordinal, never aligned, resized, cropped or masked to hide
mismatches. Sizes and coordinates are in PDF points. If page counts differ, review
the complete page sets; ordinal pairing is not a claim of semantic correspondence.
Character matching reuses extraction sequence alignment; repeated text, ligatures,
extraction order and outlines can make it ambiguous. Counts alone do not prove text
position fidelity. `characters` preserves origins, bounding boxes, font and size.

`border_strokes` identifies axial line strokes; other border representations
(e.g. filled rectangles), curves and shapes appear in `other_paths`. PDF has no
reliable Excel cell/shape semantics. All paths retain paint attributes and geometry;
image evidence includes decoded payload digest, bbox and transform, excluding xrefs.
These are diagnostics, not automatic claims that a different PDF primitive is a bug.

PDF metadata, object IDs, compression and stream arrangement do not affect the
appearance verdict. Decoded structure is compared separately from pixels. If
page geometry and pixels match at both tested DPIs, status is `matched` even when
implementation structure differs; structure/text differences remain in the report.
This is a match at the tested resolutions, not a universal guarantee.

Font-family differences are flagged as an environment/root-cause candidate, never
an automatic excuse. Raw changed-pixel ratio, channel difference >16 ratio,
MAE/max and a 0.25pt Gaussian-blur MAE help investigate antialiasing. The blurred
image is **not** an acceptance mask. Any raw pixel difference remains
`review-required`; no tolerance is calibrated without real Excel evidence.
A visible difference does not automatically prove a library defect. First inspect
fonts/fallback, export/print settings, cached values and the documented Core scope
(one regular font, omitted shapes/conditional features etc.). Preserve the raw
finding even when a human identifies an expected Core limitation.

## Target XLSX files

`cases.json` pins all 22 original file hashes and visible sheet order. Do not
regenerate the fixtures for Excel export. The following paths are relative to the
repository root.

| Case | XLSX | Visible sheets | Requested fonts |
| --- | --- | --- | --- |
| 01-japanese | [`tests/ExcelRenderer.Tests/SampleInputs/01-japanese.xlsx`](../../../tests/ExcelRenderer.Tests/SampleInputs/01-japanese.xlsx) | 日本語 | Calibri, Noto Sans JP |
| 02-image | [`tests/ExcelRenderer.Tests/SampleInputs/02-image.xlsx`](../../../tests/ExcelRenderer.Tests/SampleInputs/02-image.xlsx) | 画像 | Calibri, Noto Sans JP |
| 03-wrapped-text | [`tests/ExcelRenderer.Tests/SampleInputs/03-wrapped-text.xlsx`](../../../tests/ExcelRenderer.Tests/SampleInputs/03-wrapped-text.xlsx) | 折り返し | Calibri, Noto Sans JP |
| 04-text-decoration | [`tests/ExcelRenderer.Tests/SampleInputs/04-text-decoration.xlsx`](../../../tests/ExcelRenderer.Tests/SampleInputs/04-text-decoration.xlsx) | 文字装飾 | Calibri, Noto Sans JP |
| 05-borders | [`tests/ExcelRenderer.Tests/SampleInputs/05-borders.xlsx`](../../../tests/ExcelRenderer.Tests/SampleInputs/05-borders.xlsx) | 罫線 | Calibri, Noto Sans JP |
| 06-layout-and-pagination | [`tests/ExcelRenderer.Tests/SampleInputs/06-layout-and-pagination.xlsx`](../../../tests/ExcelRenderer.Tests/SampleInputs/06-layout-and-pagination.xlsx) | ページ設定 | Calibri, Noto Sans JP |
| 07-multiple-sheets | [`tests/ExcelRenderer.Tests/SampleInputs/07-multiple-sheets.xlsx`](../../../tests/ExcelRenderer.Tests/SampleInputs/07-multiple-sheets.xlsx) | 売上, 在庫 | Calibri |
| 08-print-scaling | [`tests/ExcelRenderer.Tests/SampleInputs/08-print-scaling.xlsx`](../../../tests/ExcelRenderer.Tests/SampleInputs/08-print-scaling.xlsx) | 75%縮小 | Calibri, Noto Sans JP |
| 09-cell-border | [`tests/ExcelRenderer.Tests/SampleInputs/09-cell-border.xlsx`](../../../tests/ExcelRenderer.Tests/SampleInputs/09-cell-border.xlsx) | Sheet1 | 游ゴシック |
| cell-argb | [`tests/ExcelRenderer.Tests/SampleInputs/CellArgb/10-cell-argb.xlsx`](../../../tests/ExcelRenderer.Tests/SampleInputs/CellArgb/10-cell-argb.xlsx) | ARGB | Calibri, Noto Sans JP |
| border-conflicts | [`tests/ExcelRenderer.Tests/SampleInputs/BorderResolution/border-conflicts.xlsx`](../../../tests/ExcelRenderer.Tests/SampleInputs/BorderResolution/border-conflicts.xlsx) | Double, Colors, Merged, Defaults, Conditional | Noto Sans JP |
| richtext | [`tests/ExcelRenderer.Tests/SampleInputs/ReaderRichText/representative.xlsx`](../../../tests/ExcelRenderer.Tests/SampleInputs/ReaderRichText/representative.xlsx) | Synthetic | Calibri, Noto Sans JP |
| cf-phase1 | [`docs/validation/conditional-formatting/representative.xlsx`](../../../docs/validation/conditional-formatting/representative.xlsx) | data | Calibri |
| cf-phase2 | [`docs/validation/conditional-formatting-phase2/representative.xlsx`](../../../docs/validation/conditional-formatting-phase2/representative.xlsx) | data | Calibri |
| cf-mapped-scale | [`docs/validation/conditional-formatting-phase2/mapped-scale.xlsx`](../../../docs/validation/conditional-formatting-phase2/mapped-scale.xlsx) | data | Calibri |
| cf-phase3-icons | [`docs/validation/conditional-formatting-phase3/fixtures/icons.xlsx`](../../../docs/validation/conditional-formatting-phase3/fixtures/icons.xlsx) | Phase3 | Noto Sans JP |
| cf-phase3-layout-mapping | [`docs/validation/conditional-formatting-phase3/fixtures/layout-mapping.xlsx`](../../../docs/validation/conditional-formatting-phase3/fixtures/layout-mapping.xlsx) | Phase3 | Noto Sans JP |
| cf-phase3-legacy-bars | [`docs/validation/conditional-formatting-phase3/fixtures/legacy-bars.xlsx`](../../../docs/validation/conditional-formatting-phase3/fixtures/legacy-bars.xlsx) | Phase3 | Noto Sans JP |
| cf-phase3-x14-bars | [`docs/validation/conditional-formatting-phase3/fixtures/x14-bars.xlsx`](../../../docs/validation/conditional-formatting-phase3/fixtures/x14-bars.xlsx) | Phase3 | Noto Sans JP |
| shapes | [`docs/validation/render-finalization-performance/inputs/shapes.xlsx`](../../../docs/validation/render-finalization-performance/inputs/shapes.xlsx) | Shapes | Calibri |
| actual-shape-text | [`docs/validation/render-finalization-performance/inputs/actual-shape-text.xlsx`](../../../docs/validation/render-finalization-performance/inputs/actual-shape-text.xlsx) | Shapes | Calibri |
| japanese-grid-report | [`samples/excel/japanese-grid-report.xlsx`](../../../samples/excel/japanese-grid-report.xlsx) | 月次業務報告書 | Calibri, MS Gothic |
