# Invoice mapping sample / 請求書マッピングサンプル

`template.xlsx` and `data.json` are the actual inputs used by
[the CLI integration tests](../../../tests/ExcelRenderer.Tool.Tests/InvoiceMappingIntegrationTests.cs).
`invoice.xlsx`, `png/`, and `svg/` contain the generated sample outputs.

テンプレートと JSON は結合テストで使用する入力です。生成した XLSX・PNG・SVG も
このディレクトリに保存しています。1 明細を商品行と備考・税率行の **2 行**で繰り返します。
明細 0 件・1 件・3 件、JSON と CLR の型、および直接描画と生成 XLSX の描画の一致を検証します。

## Regenerate / 再生成

From the repository root, build the CLI and run the following commands. Use new or empty
PNG/SVG directories; rendering refuses to overwrite existing output files.
リポジトリルートで実行してください。PNG/SVG の出力先には新規または空のディレクトリを使用します。

```sh
dotnet build src/ExcelRenderer.Tool

dotnet src/ExcelRenderer.Tool/bin/Debug/net10.0/ExcelRenderer.Tool.dll xlsx \
  samples/mapping/invoice/template.xlsx --data samples/mapping/invoice/data.json \
  -o samples/mapping/invoice/invoice.xlsx

dotnet src/ExcelRenderer.Tool/bin/Debug/net10.0/ExcelRenderer.Tool.dll image \
  samples/mapping/invoice/template.xlsx --data samples/mapping/invoice/data.json \
  -o samples/mapping/invoice/png --dpi 144

dotnet src/ExcelRenderer.Tool/bin/Debug/net10.0/ExcelRenderer.Tool.dll svg \
  samples/mapping/invoice/template.xlsx --data samples/mapping/invoice/data.json \
  -o samples/mapping/invoice/svg
```

The optional `create-template.py` rebuilds the template using Python 3 and `openpyxl`.
It is not required to run the .NET tests or render this sample.
テンプレート自体の再生成には `python -m pip install openpyxl` の後、
`python samples/mapping/invoice/create-template.py` を実行できます。
.NET のテストや変換に Python は必要ありません。

## Coverage / 確認項目

| Area / 項目 | Examples / 例 |
| --- | --- |
| JSONPath | `$.customer.name`, `.customer.contacts[0].name`, `$['customer']['address.line']`, `$["meta\|info"].currency`, `@item.name` |
| Repetition / 繰り返し | `**@start-array $.items[*] as item` through `**@end-array`; two rows per item / 1 明細 2 行 |
| Types / 型 | JSON string, number, boolean, null; CLR `DateTime`, `DateTimeOffset`, `TimeSpan`, `decimal` |
| Excel formats / Excel 表示形式 | Currency / 通貨、小数、桁区切り、負数・赤字、ゼロ、`0.0%`, `0.00E+00`, `yyyy/mm/dd`, `[h]:mm:ss` |
| Mapping formats / マッピング書式 | `format("0000")`, `format("N2", "de-DE")`, `format("C0", "ja-JP")`, `date("yyyy/MM/dd")`, `date("dd MMM yyyy", "en-US")` |
| Structure / 構造 | Precomputed line totals and numeric types, merged cells, borders, fills, row heights, footer position, print area / 計算済み明細金額と数値型、結合セル、罫線、背景色、行高、合計行、印刷範囲 |

JSON ISO date strings remain text unless `date()` is explicitly applied. CLR dates and
elapsed time remain native Excel types; `[h]:mm:ss` displays times longer than 24 hours.
`format()` produces text, while Excel number formats preserve numeric cells for formulas.
Null remarks leave a blank merged remarks area. `00123` remains a string, retaining its zeros.

JSON の日付文字列は `date()` がないセルでは文字列のままです。CLR の日付と経過時間は
Excel の型を維持し、`[h]:mm:ss` は 24 時間を超える時間を表示します。
`format()` は文字列化しますが、Excel の表示形式なら数式に使用する数値を維持します。
null の備考は空欄、文字列番号 `00123` は先頭のゼロを保持します。

## Visual review / 目視確認（2026-10-09）

The saved PNG and the SVG displayed in Chromium were both inspected as images.
The three two-row details, blank second-item remarks, 10.0% / 8.0% / 0.0% tax rates,
precomputed line-item amounts, total ¥35,610.40, Japanese text, and footer formats were
visible without clipping or overlapping. The direct mapping and staged XLSX renderings
also match byte for byte in the tests at the same PNG resolution (96 DPI).
The saved sample PNG uses 144 DPI.

保存した PNG と Chromium で表示した SVG を画像として目視確認しました。
3 件の 2 行明細、2 件目の空欄備考、各税率、明細金額、合計 ¥35,610.40、日本語、
末尾の書式例に欠けや重なりはありませんでした。テストでは PNG の解像度を 96 DPI に揃え、
直接マッピングと生成済み XLSX の描画結果がバイト単位で一致することも確認します。
保存したサンプル PNG は 144 DPI です。

### Current rendering limits / 現在の描画上の制約

- `[Red]` is retained in XLSX number formats, but PNG/SVG currently use the cell font
  color rather than the color directive in the number format. Negative values in these
  images therefore use the normal text color.
- The CLR elapsed-time test uses exactly 27 hours. With the current ClosedXML 0.105.1
  formatting dependency, 27.5 hours with `[h]:mm:ss` can display as `27:29:60` instead
  of `27:30:00`. The sample JSON duration is text (`27:30`) and is unaffected.

- `[Red]` の指定は XLSX に維持されますが、PNG/SVG は現在セルのフォント色を使用するため、
  この画像の負数は通常の文字色です。
- CLR の経過時間テストは 27 時間です。現在の ClosedXML 0.105.1 の書式処理では、
  27.5 時間を `[h]:mm:ss` で表示すると `27:30:00` ではなく `27:29:60` になる場合があります。
  サンプル JSON の時間は文字列 `27:30` なので、この問題の影響を受けません。

## Page-break sample / 改ページサンプル

[Explicit invoice page breaks](page-breaks/README.md) includes a separate template,
JSON, generated XLSX, and three pages each of PNG/SVG. It tests nested two-row details,
an empty detail block, break positions after expansion, and no extra leading page.

[改ページサンプル](page-breaks/README.md)には別テンプレート・JSON・生成済み XLSX と
PNG/SVG 各 3 ページを保存しています。入れ子の 2 行明細、空の明細ブロック、
展開後の改ページ位置、先頭に余分なページが出ないことを検証します。

Repeated cells do not support Excel formulas. Supply each `lineTotal` in the input;
formulas and defined names outside repeated regions retain the documented reference behavior.

繰り返すセルの Excel 数式は非対応です。各明細の `lineTotal` は入力側で計算して渡します。
繰り返し外の数式・定義名にはガイド記載の参照制約が適用されます。
