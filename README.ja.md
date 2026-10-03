# ExcelRenderer

> PDF 出力では通常文字を検索可能なテキストとして維持しますが、対応する漢字 IVS は format 14 で選択された字形を保持するためベクター輪郭として出力します。輪郭化された IVS 部分はテキストとして検索・コピーできません。

[English](README.md) | 日本語

Excel ワークブックを読み込み、レイアウト計算を経て PDF またはページごとの PNG・SVG を生成する .NET ライブラリです。

Excel を直接 PDF へ描画するのではなく、次の中間モデルを段階的に生成します。

```text
Excel (.xlsx)
    ↓
ReportDocument
    ↓
RenderDocument
    ↓
DrawCommand
    ↓
PDF / PNG / SVG
```

読み込み、レイアウト計算、描画命令生成、出力を分離することで、処理内容を理解しやすくし、将来的な機能追加や出力先の追加を行いやすい構成にしています。

現在は MVP 段階です。ライブラリに加えて、PDF・PNG・SVG・Markdownへ変換するコマンドラインツールを提供します。

## インストール

ライブラリ、任意のフォント、コマンドラインツールは別々の NuGet パッケージです。NuGet.org に公開された後、以下のコマンドでインストールできます。CLI はライブラリとフォントパッケージを依存関係としてインストールします。

### ライブラリ（NuGet）

.NET Standard 2.1 と互換性のあるアプリケーションのプロジェクトディレクトリで実行します。

```bash
dotnet add package ExcelRenderer
dotnet add package ExcelRenderer.Fonts  # 日本語フォントが必要な場合
```

`ExcelRenderer` はアプリケーション側でフォントを用意する場合、単独で使用できます。
`ExcelRenderer.Fonts` は通常描画用 Noto Sans JP Regular TTF、IVS 用 IPAmj 明朝、Noto Color Emoji の
フォントリソースだけを追加する任意パッケージであり、ライブラリ API の利用には
必須ではありません。フォントを再配布する場合は、それぞれのライセンスに従って
ください。詳細は[サードパーティ通知](THIRD-PARTY-NOTICES.md)を参照してください。

インストール後、次の API で変換できます。

```csharp
using ExcelRenderer;

await ExcelConverter.ConvertToPdfAsync("input.xlsx", "output.pdf");
await ExcelConverter.ConvertToImagesAsync("input.xlsx", "./images");
await ExcelConverter.ConvertToSvgAsync("input.xlsx", "./svg-output");
await ExcelConverter.ConvertToMarkdownAsync("input.xlsx", "output.md");
```

ストリーム連携には `RenderAsync` を使用できます。入力は現在位置から読み取り、入力
ストリームと `SingleStreamOutputSink` の出力ストリームは閉じません。ページごとの
PNG/SVG には `DirectoryOutputSink` を使用します。`RenderRequest` では完全一致の
シート名（指定順）と PDF/PNG/SVG の文書ページを選択できます。Markdown はページ
選択を受け付けません。`ConversionManifest.WriteAsync` は完了した成果物メタデータと
診断を、相対成果物名だけを含む schema version 1 の JSON として出力します。

### コマンドラインツール（dotnet tool）

.NET 10 SDK をインストールし、NuGet.org からツールを取得します。

```bash
dotnet tool install --global ExcelRenderer.Tool
excelrenderer --help
```

パッケージ名は `ExcelRenderer.Tool`、実行コマンド名は `excelrenderer` です。CLI は
`ExcelRenderer` と `ExcelRenderer.Fonts` に依存するため、これらを個別にインストールする
必要はありません。これらのコマンドは、NuGet のパッケージソースで NuGet.org が有効に
なっていることを前提とします。

```bash
excelrenderer pdf input.xlsx -o output.pdf
excelrenderer image input.xlsx -o ./images
excelrenderer svg input.xlsx -o ./svg-output
excelrenderer md input.xlsx -o output.md
```

基本構文は次のとおりです。`input.xlsx` は位置引数で、必須の `--output` は `-o` と省略できます。

```text
excelrenderer <command> <input.xlsx> --output <path> [options]
```

| コマンド | 出力 | コマンド固有の主なオプション |
| --- | --- | --- |
| `pdf` | 1 個の PDF | `--sheet <シート名>` |
| `image` | 印刷ページごとの PNG | `--sheet <シート名>`、`--dpi <数値>`（既定値 `144`） |
| `svg` | 印刷ページごとの自己完結 SVG | `--sheet <シート名>` |
| `markdown` / `md` | 1 個の Markdown と任意の抽出画像 | `--sheet`、`--image-dir`、`--[no-]images`、`--[no-]cell-addresses`、`--[no-]formulas`、`--[no-]layout-detection`、`--[no-]region-detection` |
| `render` | PDF ファイル、または PNG/SVG/Markdown の出力ディレクトリ | `--format pdf\|png\|svg\|markdown` と選択、レイアウト、診断、マニフェスト、フォント方針の各オプション |

すべてのコマンドで次のフォント指定を使用できます。

| オプション | 説明 |
| --- | --- |
| `--font-dir <ディレクトリ>` | 追加ディレクトリを再帰的に検索します。複数回指定、または 1 回に複数の値を指定できます。 |
| `--font-file <ファイル>` | フォントファイルを直接登録します。複数指定時はコマンド行の順に使用します。.ttf、.tte、.otf の TrueType/OpenType 内容を使用できます。 |
| `--fallback-font <ファミリー名>` | 既定のフォールバック一覧を、指定順のファミリーで置き換えます。複数回指定して優先順を作れます。 |
| `--no-system-fonts` | OS のフォントディレクトリを検索しません。明示ファイル、追加ディレクトリ、CLI 同梱フォントは引き続き使用できます。 |
| `--font-policy bundled\|requested` | 同梱の互換フォント（既定）と、要求・明示指定したフォントのどちらを優先するか選びます。 |
| `--ivs-font-style gothic\|mincho` | IVS に使用する同梱フォントの書体を選びます（既定値 `gothic`）。 |

これらは PDF、PNG、SVG の字形選択に影響します。Markdown は字形を描画しませんが、共通のフォント引数を持つスクリプトでコマンドを切り替えられるよう同じ指定を受け付けます。空白を含むパスは引用符で囲んでください。コンテナーで再現可能な出力にする例:

```bash
excelrenderer pdf report.xlsx -o report.pdf \
  --font-dir /app/fonts \
  --font-file /app/company-fonts/ReportSans.ttf \
  --fallback-font "Noto Sans JP" \
  --fallback-font "Liberation Sans" \
  --no-system-fonts
```

コマンド一覧は `excelrenderer --help`、各コマンドの完全なオプション一覧は `excelrenderer <command> --help` で確認できます。

既にグローバルインストールしている場合は、次のコマンドで更新します。

```bash
dotnet tool update --global ExcelRenderer.Tool
```

プロジェクト単位で管理する場合は、利用するリポジトリでローカルインストールします。`dotnet new tool-manifest` は、既存のマニフェストがない場合だけ実行してください。

```bash
dotnet new tool-manifest
dotnet tool install --local ExcelRenderer.Tool
dotnet tool run excelrenderer --help
```

`.config/dotnet-tools.json` をコミットすると、他の開発者は `dotnet tool restore` で同じバージョンをインストールできます。

上記のフォント指定はすべてのコマンドで使用できます。
`--font-file` はファイルを直接読み、内部のファミリー名と書体を登録します。このため、
拡張子ではなく内容が SkiaSharp で有効な TrueType/OpenType であれば `.ttf`、Windows EUDC の
`.tte`、`.otf` を同じ入口から指定できます。例:
`--font-file /app/fonts/report.ttf --font-file "C:\Windows\Fonts\EUDC.TTE"`。
BMP 私用領域 (U+E000–U+F8FF) は要求フォント、指定ファイル（コマンド行の順）、通常の
フォールバックの順に調べます。どのフォントにも字形がなければ `MissingPrivateUseGlyph` を
報告して U+FFFD を描画し、`--strict` または `--warnings-as-errors MissingPrivateUseGlyph` で
変換を失敗させられます。ファイルは読取可能かつ有効でなければならず、ライセンスと PDF
埋め込み条件の確認は利用者の責任です。Markdown は字形を描画しないため、この指定の影響を
受けません。既定の
`bundled` は既存出力との互換性のため同梱フォントを優先し、`requested` は指定フォントと
追加ディレクトリを設定済みフォールバックおよび同梱フォントより先に検索します。フォール
バックは Unicode テキスト要素単位で決定するため、サロゲートペアと結合文字列は分断しません。

統合 `render` コマンドでは、選択した各シートを印刷ページの余白・改ページ・タイトル・ヘッダーなしの連続画像として出力できます。

```bash
excelrenderer render input.xlsx -o ./continuous-images --format svg --image-layout continuous
```

連続レイアウトは PNG と SVG のみで、`--pages` とは併用できません。印刷範囲ではなくシートの使用範囲を使います。PNG の連続出力は、RGBA bitmap とエンコード時の追加メモリを安全に抑えるため、既定で 1 億ピクセル（bitmap 本体で約 381 MiB）の上限を適用します。より大きいキャンバスが必要な場合も、`RenderRequest.MaxPngPixels` には安全な有限値を指定してください。

## 対応範囲

- ClosedXML による `.xlsx` の読み込み
- セル文字列、フォント、文字サイズ、配置、折り返しの読み込み
- 結合セル、列幅、行高の読み込み
- 非表示行・非表示列の除外
- 印刷領域、用紙サイズ、向き、余白、拡大縮小設定の読み込み
- 背景色、罫線、セル文字列の描画
- 行・列境界を基準としたページ分割
- PNG、JPEG などのワークシート画像の描画
- ヘッダー、フッター文字列の描画
- PDFsharp による PDF 出力
- SkiaSharp によるページごとの PNG 出力と画像のデコード

## 全体の処理フロー

ライブラリ内部では、次の順番で処理します。

```text
Excelファイル
    │
    ▼
ExcelReader
    │
    ▼
ReportDocument / ReportSheet
    │
    ▼
ReportLayoutEngine
    │
    ├─ NormalizePass
    ├─ ResolvePrintAreaPass
    ├─ HiddenRowColumnPass
    ├─ ColumnLayoutPass
    ├─ RowLayoutPass
    ├─ TextMeasurePass
    ├─ CellBoundsPass
    └─ PaginationPass
    │
    ▼
RenderDocument
    │
    ▼
DrawCommandGeneratorPass
    │
    ├─ FillRectangleCommand
    ├─ DrawBorderCommand
    ├─ DrawTextCommand
    └─ DrawImageCommand
    │
    ▼
PdfSharpRenderer / PngRenderer
    │
    ▼
PDF / PNG
```

処理は大きく次の 4 段階に分かれています。

1. Excel ファイルの読み込み
2. レイアウト計算
3. 描画コマンドの生成
4. PDF または PNG への描画

## 1. Excel ファイルの読み込み

`ExcelReader` が ClosedXML を使用して Excel ワークブックを読み込み、ライブラリ独自のモデルである `ReportDocument` を生成します。

```text
Excel Workbook
    ↓
ExcelReader
    ↓
ReportDocument
    └─ ReportSheet
        ├─ Cells
        ├─ Rows
        ├─ Columns
        ├─ MergedCells
        ├─ Images
        └─ PageSettings
```

`ReportDocument` は複数の `ReportSheet` を保持します。この段階では Excel から取得した情報を保持しますが、PDF 上の具体的な座標やページ番号はまだ決定しません。

読み込み処理とレイアウト処理を分離しているため、将来的には ClosedXML 以外の入力元を追加することも可能です。同じ `ReportDocument` を生成できれば、CSV、JSON、データベース、独自帳票定義、他の Excel 読み込みライブラリなどにも対応できます。

## 2. レイアウト計算

`ReportLayoutEngine` は、複数のレイアウト Pass を順番に実行します。

```csharp
public interface IReportLayoutPass
{
    void Execute(ReportLayoutContext context);
}
```

各 Pass は共有される `ReportLayoutContext` を参照・更新します。

```text
ReportSheet
    ↓
ReportLayoutContext
    ↓
Pass 1 → Pass 2 → Pass 3 → ...
    ↓
RenderDocument
```

### ReportLayoutContext

`ReportLayoutContext` はレイアウト計算中の状態を保持するオブジェクトです。主に次の情報を保持します。

| プロパティ | 内容 |
| --- | --- |
| `Sheet` | レイアウト対象のワークシート |
| `TextMeasurer` | 文字列の描画サイズを計測する実装 |
| `PrintArea` | 解決済みの印刷範囲 |
| `VisibleColumns` | 描画対象となる列 |
| `VisibleRows` | 描画対象となる行 |
| `ColumnLayouts` | 各列の位置と幅 |
| `RowLayouts` | 各行の位置と高さ |
| `TextSizes` | セル文字列の計測結果 |
| `CellLayouts` | 各セルの描画領域 |
| `RenderDocument` | 最終的なページレイアウト |

各 Pass は前の Pass が作成した情報を利用し、次の Pass に必要な情報を追加します。この方式により、大きなレイアウト処理を 1 つのクラスに集中させず、責務ごとに分割しています。

### レイアウト Pass の実行順序

現在の `ReportLayoutEngine` は、次の順番で Pass を実行します。

```csharp
new NormalizePass(),
new ResolvePrintAreaPass(),
new HiddenRowColumnPass(),
new ColumnLayoutPass(),
new RowLayoutPass(),
new TextMeasurePass(),
new CellBoundsPass(),
new PaginationPass()
```

Pass には依存順序があります。例えば、セルの描画領域には列幅と行高が必要であり、ページ分割にはセルの位置と用紙設定が必要です。新しい Pass を追加する場合は、入力として必要な情報がどの Pass で作られるかを確認し、適切な位置に組み込みます。

### NormalizePass

入力されたワークシート情報を後続の処理で扱いやすい状態に正規化します。Excel 固有の表現差異を後続 Pass へ持ち込まず、セル・行・列情報を共通の前提へ揃えます。

### ResolvePrintAreaPass

ワークシートの印刷領域を解決します。印刷領域が設定されている場合はその領域を使用し、設定されていない場合はシート内のデータ範囲などから描画対象範囲を決定します。結果は `ReportLayoutContext.PrintArea` に保存されます。

### HiddenRowColumnPass

印刷領域内の行・列から非表示行と非表示列を除外し、結果を `VisibleRows` と `VisibleColumns` に保存します。以降の処理では非表示の行・列は幅や高さを持たないものとして扱います。

### ColumnLayoutPass / RowLayoutPass

`ColumnLayoutPass` は描画対象となる各列の開始位置、幅、累積位置を計算し、`ColumnLayouts` に保存します。`RowLayoutPass` は同様に各行の開始位置、高さ、累積位置を計算し、`RowLayouts` に保存します。

Excel の列幅と PDF 上のポイント値は単位が異なるため、描画用の寸法への変換もこの段階で行います。

### TextMeasurePass

各セルの文字列を描画した場合に必要となるサイズを、`ITextMeasurer` で計測して `TextSizes` に保存します。主にフォントファミリー、フォントサイズ、太字などのスタイル、折り返し、セル幅、改行を考慮します。

PDFsharp を使用する場合は `PdfSharpTextMeasurer` を指定します。計測をインターフェースとして分離しているため、PDFsharp 以外の計測方法にも差し替えられます。

### CellBoundsPass

列レイアウトと行レイアウトを組み合わせ、各セルの PDF 上の座標と描画領域を `CellLayouts` に保存します。結合セルでは対象となる複数の行・列をまとめて 1 つの描画領域として扱います。

### PaginationPass

用紙サイズ、向き、余白、拡大縮小設定、セル位置を使用してページ分割を行い、最終的な `RenderDocument` を生成します。倍率指定（例: 75%）と「横・縦を指定ページ数に合わせる」の両方を反映し、セル、文字、罫線、画像を同じ比率で拡大縮小します。`RenderDocument` は複数の `RenderPage` を保持し、ページ番号、ページ内のセル・画像、ヘッダー・フッター、各要素のページ内座標を含みます。

ページ分割は行・列の境界を基準として行います。セルや結合セルの途中では分割せず、配置可能な行・列の単位で改ページ位置を決定します。

## 3. 描画コマンドの生成

レイアウト計算後の `RenderDocument` は、まだ PDFsharp に直接依存していません。`DrawCommandGeneratorPass` が `RenderDocument` を読み取り、描画内容を `DrawCommand` に変換します。

ページごとに、おおむね次の順番でコマンドを生成します。

1. 背景
2. 罫線
3. セル文字列
4. 画像
5. ヘッダー、フッター

描画順序は要素の重なり方に影響します。例えば、背景を文字列より後に描画すると文字列が隠れてしまうため、背景を先に生成します。

- `FillRectangleCommand`: セルの背景色を描画
- `DrawBorderCommand`: セルの罫線を描画
- `DrawTextCommand`: セル文字列、ヘッダー、フッターを描画
- `DrawImageCommand`: ワークシート上の画像を描画

描画コマンドを中間モデルとして持つことで、レイアウト計算と実際の描画処理を分離できます。レイアウト処理を変更せずにレンダラーを追加したり、描画コマンドを検査するテストを作成したりできます。

## 4. PDF / PNG / SVG への描画

`PdfSharpRenderer` は生成された `DrawCommand` を順番に処理して PDF を作成します。

| 描画コマンド | PDFsharp での処理 |
| --- | --- |
| `FillRectangleCommand` | 矩形の塗りつぶし |
| `DrawBorderCommand` | 線の描画 |
| `DrawTextCommand` | 文字列の描画 |
| `DrawImageCommand` | 画像の描画 |

`PdfSharpRenderer` の責務は、抽象的な描画コマンドを PDFsharp の API 呼び出しへ変換することです。画像データは SkiaSharp でデコードしてから PDF へ描画します。

`PngRenderer` は同じ `DrawCommand` を SkiaSharp で描画し、各ページを独立した PNG にします。用紙寸法と描画座標はポイント単位のまま受け取り、既定の 96 DPI（または指定した DPI）でピクセルへ変換します。PNG は複数ページを格納できないため、複数ページの出力にはページ番号を受け取る出力ストリームファクトリを使用します。

`SvgRenderer` は PNG と描画処理を共有し、ポイント単位のままページごとの自己完結 SVG を生成します。文字は生成時のフォントでベクターパス化され、画像はファイル内へ埋め込まれます。このため閲覧側に日本語フォントは不要ですが、文字検索・コピーはできません。パス化は編集防止機能ではなく、複雑な文字体系やカラーフォントの完全な再現も保証しません。

## 利用方法

ライブラリを参照し、`ExcelReader`、`ReportLayoutEngine`、`DrawCommandGeneratorPass`、`PdfSharpRenderer` の順に使用します。

```csharp
using ExcelRenderer.Drawing;
using ExcelRenderer.Excel;
using ExcelRenderer.Layout;
using ExcelRenderer.PdfSharp;

var document = new ExcelReader().Read("report.xlsx");
var layoutEngine = new ReportLayoutEngine(new PdfSharpTextMeasurer());
var commandGenerator = new DrawCommandGeneratorPass();
var renderer = new PdfSharpRenderer();
var sheet = document.Sheets[0];

var renderDocument = layoutEngine.Layout(sheet);
var commands = commandGenerator.Generate(renderDocument);

using var output = File.Create("report.pdf");
renderer.Render(commands, sheet.PageSettings, output);
```

`ExcelReader` は、xlsx が明示倍率とページ数への適合のどちらを使用するかを
`PageSettings.ScaleMode` に保持します。ページ数への適合では 100% を超えて拡大しません。
`PageSettings` を直接生成して `ScaleMode` を指定しない場合は、正の `Scale` を優先する従来の
規則を維持します。位置引数の既定倍率よりページ数指定を優先するには、モードを明示します。

```csharp
var settings = new PageSettings(FitToPagesWide: 1)
{
    ScaleMode = PrintScaleMode.FitToPages,
};
```

xlsx の個別列幅と既定列幅は、Excel の基準である固定96 DPIのraw値から変換します。PNGの出力DPIは
シートの幾何には影響しません。Normalスタイルのstyle XFからフォントをたどり、themeのmajor/minor
指定を解決したうえで、設定されたフォントマネージャーにより0～9を計測します。計測できない場合は
最大数字幅7pxを使用し、変換診断へ`MaximumDigitWidthFallback`を記録します。

離れた複数の印刷範囲は外接矩形へ結合せず、個別にページ化します。明示倍率では保存された行・列の
手動改ページとページ順を反映し、Fitでは手動改ページを無視します。全領域を結合したページ番号を一度だけ
連番化するため、ページフィールドとPDF/PNG/SVGのページ選択は同じ番号を使います。複数ページに交差する
オブジェクトは同じ元配置から各ページへ配置し、物理用紙だけでなく各ページの本文Viewportでクリップします。
保存された水平・垂直中央配置ではセル、オブジェクト、本文clipを同じ量だけ移動します。連続出力では印刷設定を
適用しません。セルのインデント、内容余白、通常回転、文字要素を積むTopToBottomはPDF、PNG、SVGへ引き継ぎます。

折り返しセル文字列はレイアウト段階で行と解決済みフォントrunへ確定し、書記素単位の強制分割と
明示改行の情報を保持します。確定結果を描画命令まで渡すため、PDF、PNG、SVGが個別に異なる位置で
再改行することはありません。

DrawingML画像ではoneCell、twoCell、absoluteアンカーをpoint単位で解決し、marker offset、extent、`editAs`、
source crop、回転、flip、描画順とともに保持します。source cropは描画先Boundsを変更せず、両方の
レンダラーバックエンドで適用します。

`ReportDocument` には複数シートを保持できます。PDF を作成する対象シートを呼び出し側で選択し、シートごとにレイアウトから描画までの処理を行ってください。

```csharp
foreach (var sheet in document.Sheets)
{
    var renderDocument = layoutEngine.Layout(sheet);
    var commands = commandGenerator.Generate(renderDocument);
    var fileName = $"{sheet.Name}.pdf";

    using var output = File.Create(fileName);
    renderer.Render(commands, sheet.PageSettings, output);
}
```

### PNG として出力する

PDF と同じレイアウトおよび描画コマンドを `PngRenderer` に渡します。次の例では `report-1.png`、`report-2.png` のようにページごとのファイルを作成します。出力ストリームは各ページの描画後にレンダラーが破棄します。

```csharp
using ExcelRenderer.SkiaSharp;

var pngRenderer = new PngRenderer();
pngRenderer.Render(
    commands,
    sheet.PageSettings,
    pageNumber => File.Create($"report-{pageNumber}.png"),
    dpi: 144);
```

1 ページだけを既存のストリームへ書き込む場合は `RenderPage` を使用します。この場合、渡したストリームはレンダラーが破棄しません。

```csharp
using var output = File.Create("report.png");
pngRenderer.RenderPage(
    commands.Where(command => command.PageNumber == 1),
    sheet.PageSettings,
    output);
```

### SVG として出力する

同じ描画コマンドから `report-1.svg`、`report-2.svg` のようにページごとのファイルを作成します。SVG の寸法と座標は PDF ポイントです。

```csharp
var svgRenderer = new SvgRenderer();
svgRenderer.Render(
    commands,
    sheet.PageSettings,
    pageNumber => File.Create($"report-{pageNumber}.svg"));
```

### フォントファイルの指定

PDF と PNG の変換では、任意の `ExcelRenderer.Fonts` パッケージが導入されていれば、
埋め込まれた Noto Sans JP Regular TTF、IVS 用の IPAmj 明朝、および Noto Color Emoji を使用できます。単体の絵文字と VS16 付き絵文字はカラーで描画し、PDF と SVG には画像として埋め込みます。ZWJ などの複合絵文字には別途シェーピングが必要です。未導入
の場合は登録済みまたは OS のフォントを解決します。PDFsharp で外部フォントを使う
場合は、PDFsharp がフォントを使用する前にフォントリゾルバーを設定してください。
指定するファミリー名は、Excel のセルに設定されたフォント名と一致させます。

```csharp
using PdfSharp.Fonts;
using ExcelRenderer.PdfSharp;

GlobalFontSettings.FontResolver = new PdfSharpFontResolver(
    "Noto Sans JP",
    "/app/fonts/NotoSansJP-Regular.ttf");
```

フォントリゾルバーはアプリケーションドメインごとに一度だけ、PDFsharp がフォントを使用する処理より前に設定します。

## 設計方針

Excel から PDF や PNG を生成する処理を 1 つの巨大な変換処理にせず、入力の解釈、レイアウト計算、描画命令の生成、出力形式への描画に分割しています。

- レイアウト計算は複数の Pass に分割し、各 Pass は基本的に 1 つの目的だけを持つ
- Pass 間の情報は `ReportLayoutContext` を介して受け渡す
- `ReportLayoutEngine` は PDFsharp を直接操作せず、`RenderDocument` と `DrawCommand` を生成する
- 描画内容をコマンドとして表現し、単体テスト、描画順序の変更、別レンダラー、デバッグ出力を容易にする

## 拡張方法

### 新しいレイアウト処理を追加する

新しいレイアウト処理は `IReportLayoutPass` を実装するクラスとして追加し、`ReportLayoutEngine` の Pass 一覧へ適切な順序で登録します。

```csharp
using ExcelRenderer.Abstractions;
using ExcelRenderer.Layout;

public sealed class CellPaddingPass : IReportLayoutPass
{
    public void Execute(ReportLayoutContext context)
    {
        // context.CellLayouts などを参照・更新する
    }
}
```

```csharp
new CellBoundsPass(),
new CellPaddingPass(),
new PaginationPass()
```

現在の実装では Pass 一覧は `ReportLayoutEngine` 内で構築されています。Pass を追加する場合は、クラスの作成に加えて登録順序を変更する必要があります。

### 新しい描画コマンドを追加する

新しい描画要素を追加する場合は、次の 3 か所を拡張します。

1. 新しい `DrawCommand` を定義する
2. `DrawCommandGeneratorPass` でコマンドを生成する
3. `PdfSharpRenderer` でコマンドを描画する

例えば透かしは、`Watermark` 情報から `DrawWatermarkCommand` を生成し、`PdfSharpRenderer` で描画する構成にできます。

### 新しい出力形式を追加する

`RenderDocument` または `DrawCommand` を入力として、新しいレンダラーを追加できます。出力先として SVG、HTML Canvas、PNG、プレビュー画面、デバッグ用 JSON などが考えられます。

```text
DrawCommand
    ├─ PdfSharpRenderer
    ├─ PngRenderer
    ├─ SvgRenderer
    ├─ CanvasRenderer
    └─ DebugJsonRenderer
```

### 文字列計測方法を差し替える

文字列計測は `ITextMeasurer` として分離されています。PDFsharp、SkiaSharp、ブラウザー相当、テスト用の固定サイズなど、用途に応じた実装を追加できます。

## 拡張例

- SVG データを `ReportImage` に保持し、`DrawSvgCommand` または SVG 対応レンダラーで描画する
- `DrawCommandGeneratorPass` に `DrawWatermarkCommand` を追加して透かしを描画する
- `CustomPaginationPass` で特定の行や帳票セクション単位の改ページを追加する
- `DrawDebugBoundsCommand` でセル境界やページ領域を表示する

## 拡張時の注意点

- Pass の順序には依存関係があります。`CellBoundsPass` は `ColumnLayouts` と `RowLayouts` を使用するため、レイアウト Pass より前には実行できません。
- 新しい Pass では、必要なプロパティが設定済みであることを前提にしすぎず、必要に応じて未設定状態を検証します。
- `DrawCommand` の順番はそのまま描画順序になります。新しいコマンドをどの要素の前後に配置するかを明確にします。
- PDFsharp 固有の処理は `PdfSharpRenderer` や `ExcelRenderer.PdfSharp` 名前空間内に閉じ込めます。

## 制約

- Excel の数式を完全には再現しません。
- Excel のグラフには対応していません。
- 条件付き書式を完全には再現しません。
- すべての用紙サイズには対応していません。
- ヘッダー、フッターのすべての書式指定には対応していません。
- ページ分割は行・列の境界で行い、結合セル、行、列の途中では分割しません。
- TopToBottomはUnicode文字要素を縦に積む方式です。本格的な縦組み字形選択と日本語禁則の完全一致は未対応です。
- システムフォントを使用する場合は、実行環境にフォントをインストールする必要があります。
- Excel、PDF、PNG では文字列計測や描画方式が異なるため、完全に同一の見た目になることは保証しません。

## 開発

必要な SDK は .NET 10 です。テストはリポジトリのルートで実行します。

```bash
dotnet test ExcelRenderer.slnx
```

ソースコードは `src/ExcelRenderer`、テストコードは `tests/ExcelRenderer.Tests` にあります。

```text
src/ExcelRenderer
├─ Abstractions
│  ├─ IReportLayoutPass
│  └─ ITextMeasurer
├─ Drawing
│  ├─ DrawCommand
│  └─ DrawCommandGeneratorPass
├─ Excel
│  └─ ExcelReader
├─ Layout
│  ├─ ReportLayoutEngine
│  ├─ ReportLayoutContext
│  └─ 各種 Layout Pass
├─ Model
│  ├─ ReportDocument
│  ├─ ReportSheet
│  └─ RenderDocument
├─ PdfSharp
   ├─ PdfSharpRenderer
   ├─ PdfSharpTextMeasurer
   └─ PdfSharpFontResolver
└─ SkiaSharp
   └─ PngRenderer
```

機能を追加する際は、既存クラスへ複数の責務を追加するのではなく、新しい読み込み処理、新しいレイアウト Pass、新しい描画コマンド、新しいレンダラー、または新しい抽象インターフェースとして分離することを基本方針とします。

## ライセンス

ExcelRenderer は [MIT License](LICENSE) で提供されます。

日本語フォントと Noto Color Emoji は任意の `ExcelRenderer.Fonts` パッケージに収録されます。ライブラリで使用する場合は `dotnet add package ExcelRenderer.Fonts` を追加してください。未導入の場合は登録済みまたはシステムのフォントを使います。CLI はフォントパッケージに依存します。Noto Sans JP と Noto Color Emoji には SIL Open Font License 1.1、IPAmj 明朝には IPA Font License Agreement v1.0 が適用されます。`FontOptions.ReplaceIvsWithBaseCharacter` を `true` にすると、IVS を異体字セレクターのない基底文字に置換して描画できます。詳細は[サードパーティ通知](THIRD-PARTY-NOTICES.md)を参照してください。
