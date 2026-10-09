# Explicit invoice page breaks / 請求書の明示的な改ページ

This sample uses an outer `pages[*]` block and an inner `@page.items[*]` block.
Each item maps a product row and a remarks/tax-rate row. `**@page-break` in A2,
inside the outer block, moves to A1 for the first output page and to the first row
of each later page. The first directive creates no leading blank page; later ones
create only horizontal breaks because the marker is in column A.

外側の `pages[*]` ブロックにページ単位の請求書、内側の `@page.items[*]` ブロックに
2 行明細を配置します。外側ブロックの先頭にある A2 の `**@page-break` は、展開後、
最初は A1、次からは各ページの先頭行へ移動します。先頭に空白ページを作らず、
A 列なので列方向の改ページも作りません。

| Page / ページ | Detail count / 明細件数 | Output rows / 出力行 | Total / 合計 |
| --- | --- | --- | --- |
| [1 PNG](png/請求書-1.png) / [SVG](svg/請求書-1.svg) | 2 | 1–21 | ¥36,110.40 |
| [2 PNG](png/請求書-2.png) / [SVG](svg/請求書-2.svg) | 0 | 22–38 | ¥0.00 |
| [3 PNG](png/請求書-3.png) / [SVG](svg/請求書-3.svg) | 1 | 39–57 | -¥500.00 |

The XLSX has row breaks after rows 21 and 38, with no column breaks. An empty
inner array removes only its details, preserving that page's header and totals.
Fit-to-page is deliberately enabled in the template; mapping changes it to 100%
scale so manual breaks remain effective. The print area extends one blank row
beyond the outer block, anchoring its end after all repeated pages.

XLSX の行改ページは 21 行目・38 行目の後です。空の内側配列でも、そのページの
見出し・合計欄は残ります。テンプレートは 1 ページに収める設定ですが、マッピング時に
倍率 100% に切り替わることを確認します。印刷範囲の終端は外側ブロックの直後の空行まで
含め、繰り返した全ページへ追従させています。

[Integration tests](../../../../tests/ExcelRenderer.Tool.Tests/InvoicePageBreakIntegrationTests.cs)
check 1 and 3 pages, mapped data on both detail rows, precomputed line totals and numeric
types, merges, footer positions, exact XLSX break positions, and PNG/SVG page counts.
Each output page must match the same page rendered in isolation byte for byte;
this detects split details, misplaced totals, neighboring-page content, and extra pages.
The existing four-quadrant break test also covers PNG and SVG.

結合テストは 1 ページ・3 ページ、明細両行のデータ、計算済み明細金額と数値型、結合セル、
合計欄、改ページ位置、PNG/SVG のページ数を検証します。各ページを単独で生成した結果とも
バイト単位で比較し、明細の分断・合計の移動・隣ページの混入・余分なページを検出します。
既存の上下左右 4 分割の改ページテストも PNG/SVG 両方を検証します。

## Regenerate / 再生成

Run from the repository root after building the CLI. PNG/SVG output directories
must be new or empty. The optional Python script requires `openpyxl` and regenerates
both this template and its JSON from the parent invoice sample.

```sh
python samples/mapping/invoice/create-page-break-template.py

dotnet src/ExcelRenderer.Tool/bin/Debug/net10.0/ExcelRenderer.Tool.dll xlsx \
  samples/mapping/invoice/page-breaks/template.xlsx \
  --data samples/mapping/invoice/page-breaks/data.json \
  -o samples/mapping/invoice/page-breaks/invoice.xlsx

dotnet src/ExcelRenderer.Tool/bin/Debug/net10.0/ExcelRenderer.Tool.dll image \
  samples/mapping/invoice/page-breaks/template.xlsx \
  --data samples/mapping/invoice/page-breaks/data.json \
  -o samples/mapping/invoice/page-breaks/png --dpi 96

dotnet src/ExcelRenderer.Tool/bin/Debug/net10.0/ExcelRenderer.Tool.dll svg \
  samples/mapping/invoice/page-breaks/template.xlsx \
  --data samples/mapping/invoice/page-breaks/data.json \
  -o samples/mapping/invoice/page-breaks/svg
```

## Validation / 検証結果（2026-10-09）

- Rebuild: zero warnings and zero errors. All 586 solution tests passed after the mapping performance changes.
- All three saved PNG pages and all three SVG pages displayed in Chromium were
  inspected as images. Page 1 contains both complete two-row items; page 2 contains
  no items but retains its header and zero totals; page 3 contains the complete
  discount item, its remarks/0.0% tax rate, and the -¥500.00 total.
- No split details, clipping, overlapping, neighboring-page content, or extra
  leading/trailing pages were observed. Page 3 was checked again after all tests passed.

リビルドは警告・エラーとも 0 件、ソリューション全 569 テストが成功しました。
保存した PNG 全 3 ページと、Chromium で画像表示した SVG 全 3 ページを目視確認しました。
1 ページ目は 2 件とも商品行・備考行が揃い、2 ページ目は明細 0 件でも見出し・合計が残り、
3 ページ目は値引きの明細両行・税率 0.0%・合計 -¥500.00 が揃っています。
明細の分断、欠け、重なり、隣ページの混入、余分な先頭・末尾ページはありませんでした。
全テスト成功後に、行位置が最もずれる 3 ページ目を再度画像で確認しています。

Repeated cells do not support Excel formulas. Supply each `lineTotal` in the input;
formulas and defined names outside repeated regions retain the documented reference behavior.

繰り返すセルの Excel 数式は非対応です。各明細の `lineTotal` は入力側で計算して渡します。
繰り返し外の数式・定義名にはガイド記載の参照制約が適用されます。
