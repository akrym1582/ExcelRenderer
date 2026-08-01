# excel2pdf

Excel ワークブックを読み込み、レイアウト計算を経て PDF を生成する .NET ライブラリです。
現在は MVP 段階であり、コマンドラインアプリケーションは含まれません。

## 対応範囲

- ClosedXML による `.xlsx` の読み込み
- セル文字列、フォント、折り返し、結合セル、列幅、行高、非表示行・列の取り込み
- 印刷領域、用紙サイズ、向き、余白の取り込みと、セル配置・行列単位のページ分割
- 背景色、罫線、文字列を PDFsharp で描画
- ワークシートに配置された PNG/JPEG などの画像を SkiaSharp で処理して描画

## 処理の流れ

1. `ExcelReader` が ClosedXML から `ReportDocument` を作成します。
2. `ReportLayoutEngine` が正規化、印刷領域解決、非表示行列処理、列・行レイアウト、テキスト計測、セル境界計算、ページ分割を順に実行し、`RenderDocument` を作成します。
3. `DrawCommandGeneratorPass` が背景、罫線、テキスト、画像の順に描画コマンドを生成します。
4. `PdfSharpRenderer` が描画コマンドを PDF に出力します。画像は SkiaSharp でデコードします。

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

`ReportDocument` には複数シートを保持できます。PDF を作成する対象シートは呼び出し側で選択し、シートごとにレイアウトから描画までの処理を行ってください。

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

- 画像は Excel の左上アンカーを基準に描画します。ページをまたぐ画像の分割描画には対応していません。
- 複数の印刷領域が設定されている場合は、先頭の印刷領域のみを使用します。
- Excel の数式、グラフ、条件付き書式、すべての用紙サイズやヘッダー／フッターの書式指定は完全には再現しません。
- ページ分割は行・列の境界で行い、結合セルと行・列は分割しません。
- システムフォントを使用する場合は、実行環境にフォントをインストールする必要があります。

## 開発

必要な SDK は .NET 10 です。テストはリポジトリのルートで実行します。

```bash
dotnet test Excel2Pdf.slnx
```

ソースコードは `src/ReportEngine`、テストは `tests/ReportEngine.Tests` にあります。
