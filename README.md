# excel2pdf

Excel ワークブックを読み込み、レイアウト計算を経て PDF を生成する .NET ライブラリです。
現在は MVP 段階であり、コマンドラインアプリケーションは含まれません。

## 対応範囲

- ClosedXML による `.xlsx` の読み込み
- セル文字列、フォント、折り返し、結合セル、列幅、行高、非表示行・列の取り込み
- 印刷領域の解決、セル配置、行単位のページ分割
- 背景色、罫線、文字列を PDFsharp で描画

## 利用方法

ライブラリを参照し、`ExcelReader`、`ReportLayoutEngine`、`DrawCommandGeneratorPass`、`PdfSharpRenderer` の順に使用します。

```csharp
using ReportEngine.Drawing;
using ReportEngine.Excel;
using ReportEngine.Layout;
using ReportEngine.PdfSharp;

var document = new ExcelReader().Read("report.xlsx");
var layoutEngine = new ReportLayoutEngine(new PdfSharpTextMeasurer());
var renderer = new PdfSharpRenderer();

using var output = File.Create("report.pdf");
var layout = layoutEngine.Layout(document.Sheets[0]);
var commands = new DrawCommandGeneratorPass().Generate(layout);
renderer.Render(commands, document.Sheets[0].PageSettings, output);
```

`ReportDocument` には複数シートを保持できます。PDF を作成する対象シートは呼び出し側で選択してください。

### フォントファイルの指定

フォントがインストールされていない環境では、PDFsharp のフォント操作を行う前にフォントリゾルバーを設定してください。指定するファミリー名は Excel のセルに設定されたフォント名と一致させます。

```csharp
using PdfSharp.Fonts;
using ReportEngine.PdfSharp;

GlobalFontSettings.FontResolver = new PdfSharpFontResolver(
    "Noto Sans JP",
    "/app/fonts/NotoSansJP-Regular.ttf");
```

フォントリゾルバーはアプリケーション ドメインごとに一度だけ、かつ PDFsharp のフォント操作より前に設定します。

## 制約

- 画像の描画は未実装です。
- Excel の印刷設定、数式、グラフ、条件付き書式などは完全には再現しません。
- ページ分割は縦方向のみで、行を分割しません。
- システムフォントを使用する場合は、実行環境にフォントをインストールする必要があります。

## 開発

必要な SDK は .NET 10 です。テストはリポジトリのルートで実行します。

```bash
dotnet test Excel2Pdf.slnx
```

ソースコードは `src/ReportEngine`、テストは `tests/ReportEngine.Tests` にあります。
