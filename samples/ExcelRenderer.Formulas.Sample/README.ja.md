# 数式付き請求書サンプル

.NET 10 を使い、リポジトリのルートで実行します。

```sh
dotnet run --project samples/ExcelRenderer.Formulas.Sample -c Release -- /tmp/formula-invoice-3 3
```

引数は、新規または空の出力ディレクトリ、明細件数（0～1000、既定 3）、通常の TrueType フォントのパスです。フォント省略時はリポジトリの `third_party/NotoSansJP/NotoSansJP-Regular.ttf` を使います。描画に必要なネイティブ依存は本体ライブラリと同じです。

`template.xlsx` を生成し、数量・単価を Mapping で設定して明細行を展開します。数式と名前付き範囲 `Amounts` を書き換え、再計算した `invoice.xlsx` を保存し、保存済み合計値を検証します。計算順序は数量×単価 → 別シートの固定税率 `Rates!$A$1` → ROUND → SUM です。元テンプレートには意図的に古いキャッシュ 999 を設定しています。その後、Invoice シートを明示再計算モードで PDF・PNG・SVG・Markdown に出力します。外部表計算エンジンは使いません。

出力先を分けて 0・1・4 件も実行できます。

```sh
dotnet run --project samples/ExcelRenderer.Formulas.Sample -c Release -- /tmp/formula-invoice-0 0
dotnet run --project samples/ExcelRenderer.Formulas.Sample -c Release -- /tmp/formula-invoice-1 1
dotnet run --project samples/ExcelRenderer.Formulas.Sample -c Release -- /tmp/formula-invoice-4 4
```

0・1・3・4 件の期待合計はそれぞれ 0・11・66・110 です。集計範囲には残存する見出し行も含め、全明細削除後も SUM の参照範囲が有効になるようにしています。対応関数と制限は[数式ガイド](../../docs/formulas.ja.md)を参照してください。Excel 実機との完全互換を証明するサンプルではありません。
