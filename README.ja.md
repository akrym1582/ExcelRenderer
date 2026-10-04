# ExcelRenderer

> PDF 出力では通常文字を検索可能なテキストとして維持しますが、対応する漢字 IVS は format 14 で選択された字形を保持するためベクター輪郭として出力します。輪郭化された IVS 部分はテキストとして検索・コピーできません。

[English](README.md) | 日本語

## 概要

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

### 主な機能

- ClosedXML による `.xlsx` の読み込み
- セル文字列、フォント、文字サイズ、配置、折り返しの読み込み
- 結合セル、列幅、行高の読み込み
- 非表示行・非表示列の除外
- 印刷領域、用紙サイズ、向き、余白、拡大縮小設定の読み込み
- 背景色、罫線、セル文字列の描画
- 行・列境界を基準としたページ分割
- PNG、JPEG などのワークシート画像の描画
- ヘッダー、フッター文字列の描画
- 印刷範囲に依存しないシート座標での図形・画像のアンカー解決（回転後の外接矩形によるページ判定・自動使用範囲・連続キャンバス寸法、反復タイトル領域を除いた本文クリップ）。非表示行列とアンカーの相互作用は Excel 実機で未検証です
- PDFsharp による PDF 出力
- SkiaSharp によるページごとの PNG 出力と画像のデコード

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

## クイックスタート

CLI で PDF・画像・SVG・Markdown に変換します。

```bash
excelrenderer pdf input.xlsx -o output.pdf
excelrenderer image input.xlsx -o ./images
excelrenderer svg input.xlsx -o ./svg-output
excelrenderer md input.xlsx -o output.md
```

C# から呼び出す場合は、ライブラリを追加して次の 1 行で変換できます。

```csharp
await ExcelConverter.ConvertToPdfAsync("input.xlsx", "output.pdf");
```

出力例:

![方眼紙報告書のサンプル](samples/png/japanese-grid-report.png)

詳しくは [CLI の使い方](#cli-の使い方)、[C# API（高レベル）](#c-api高レベル)、[C# API（低レベル）](#c-api低レベル)を参照してください。

## CLI の使い方

### 基本構文とコマンド

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

### フォント指定

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

### 連続画像として出力する

統合 `render` コマンドでは、選択した各シートを印刷ページの余白・改ページ・タイトル・ヘッダーなしの連続画像として出力できます。

```bash
excelrenderer render input.xlsx -o ./continuous-images --format svg --image-layout continuous
```

連続レイアウトは PNG と SVG のみで、`--pages` とは併用できません。印刷範囲ではなくシートの使用範囲を使います。PNG の連続出力は、RGBA bitmap とエンコード時の追加メモリを安全に抑えるため、既定で 1 億ピクセル（bitmap 本体で約 381 MiB）の上限を適用します。より大きいキャンバスが必要な場合も、`RenderRequest.MaxPngPixels` には安全な有限値を指定してください。

## C# API（高レベル）

`ExcelConverter` が読み込み、レイアウト、描画、書き出しまでを行います。

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

## C# API（低レベル）

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
再改行することはありません。各バックエンドは確定した実効フォントサイズ、行ごとのbaselineと高さ、
選択済みrunフォント、論理run X/advanceを使用し、描画時に折返し・縮小・送り位置を再決定しません。
`TextLayout`を持たずAPIから直接作成した描画命令には、従来のレンダラ側互換経路を維持します。
この互換経路のPNG/SVGでは、指定family・weight・slantからSkiaのシステムfaceを一度選択し、
計測、折返し、縮小、描画まで同じfaceを維持します。

`TextLayoutResult.EffectiveFontSize` は互換性のため状態を区別します。従来の2引数コンストラクターで
サイズを未指定にした結果は描画命令のstyleサイズを使い、明示的な0は描画を抑止します。PDFとSkiaは
確定runで選択された実faceをメモリフォントを含めて再利用します。確定経路の下線は保存済みの行原点・
baseline・幅から一度だけ手動描画し、PDFフォント自身の下線装飾は無効にします。

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

## フォント

PDF と PNG の変換では、任意の `ExcelRenderer.Fonts` パッケージが導入されていれば、
埋め込まれた Noto Sans JP Regular TTF、IVS 用の IPAmj 明朝、および Noto Color Emoji を使用できます。単体の絵文字と VS16 付き絵文字はカラーで描画し、PDF と SVG には画像として埋め込みます。ZWJ などの複合絵文字には別途シェーピングが必要です。未導入
の場合は登録済みまたは OS のフォントを解決します。PDFsharp で外部フォントを使う
場合は、PDFsharp がフォントを使用する前にフォントリゾルバーを設定してください。
指定するファミリー名は、Excel のセルに設定されたフォント名と一致させます。

任意の同梱フォントを読み込む際は、次のファイル名（`NotoSansJP-Regular.ttf`、
`ipamjm.ttf`、`NotoColorEmoji.ttf`）をこの順で検索します。

1. `ExcelRenderer.dll` と同じディレクトリにある `*.dll` 内の埋め込みリソース。
   リソース名はファイル名と完全一致するか、`.` とファイル名で終わる必要があります。
   DLL はパスの ordinal 順に確認し、読み込みまたは検査できない DLL はスキップします。
2. `ExcelRenderer.dll` の配置ディレクトリおよびそのサブディレクトリにある、
   ファイル名が完全一致する通常ファイル。複数見つかった場合はパスの ordinal 順で
   最初のファイルを使用します。

埋め込みリソースが見つかった場合は通常ファイルより優先します。DLL の検索は再帰せず、
サブディレクトリまで検索するのは通常ファイルだけです。

```csharp
using PdfSharp.Fonts;
using ExcelRenderer.PdfSharp;

GlobalFontSettings.FontResolver = new PdfSharpFontResolver(
    "Noto Sans JP",
    "/app/fonts/NotoSansJP-Regular.ttf");
```

フォントリゾルバーはアプリケーションドメインごとに一度だけ、PDFsharp がフォントを使用する処理より前に設定します。

## アーキテクチャ

Excel を `ReportDocument`、`RenderDocument`、`DrawCommand` の順に変換し、PDF・PNG・SVG へ描画します。

```text
Excel (.xlsx) → ReportDocument → RenderDocument → DrawCommand → PDF / PNG / SVG
```

レイアウト Pass、描画コマンド、レンダラー、拡張方法、ソースコード構成の詳細は [アーキテクチャ](docs/architecture.ja.md)を参照してください。

## 制約

- Excel の数式を完全には再現しません。
- Excel のグラフには対応していません。
- 条件付き書式を完全には再現しません。
- すべての用紙サイズには対応していません。
- ヘッダー、フッターのすべての書式指定には対応していません。
- ページ分割は行・列の境界で行い、結合セル、行、列の途中では分割しません。
- TopToBottomはUnicode文字要素を縦に積む方式です。本格的な縦組み字形選択と日本語禁則の完全一致は未対応です。
- 使用範囲・印刷範囲外でもxlsx XMLに明示された行列寸法をアンカー用シート座標へ反映し、非表示行列は0ptとして扱います。非表示行列とアンカーの挙動はExcel実機と未照合です。
- システムフォントを使用する場合は、実行環境にフォントをインストールする必要があります。
- Excel、PDF、PNG では文字列計測や描画方式が異なるため、完全に同一の見た目になることは保証しません。

## 開発

必要な SDK は .NET 10 です。テストはリポジトリのルートで実行します。

```bash
dotnet test ExcelRenderer.slnx
```

ソースコードは `src/ExcelRenderer`、テストコードは `tests/ExcelRenderer.Tests` にあります。

詳細な設計資料は [アーキテクチャ](docs/architecture.ja.md)、[English](README.md) も参照してください。

## ライセンス

ExcelRenderer は [MIT License](LICENSE) で提供されます。

日本語フォントと Noto Color Emoji は任意の `ExcelRenderer.Fonts` パッケージに収録されます。ライブラリで使用する場合は `dotnet add package ExcelRenderer.Fonts` を追加してください。未導入の場合は登録済みまたはシステムのフォントを使います。CLI はフォントパッケージに依存します。Noto Sans JP と Noto Color Emoji には SIL Open Font License 1.1、IPAmj 明朝には IPA Font License Agreement v1.0 が適用されます。`FontOptions.ReplaceIvsWithBaseCharacter` を `true` にすると、IVS を異体字セレクターのない基底文字に置換して描画できます。詳細は[サードパーティ通知](THIRD-PARTY-NOTICES.md)を参照してください。

PDF 出力に使用した確定フォントは、PDFsharp のグローバル resolver の寿命に合わせてスナップショットをプロセス寿命でキャッシュします。折返し計測時のフォントファイル読込みとハッシュの反復を避けるためであり、アプリケーションでは有限で安定したフォント face 群を使用してください。
