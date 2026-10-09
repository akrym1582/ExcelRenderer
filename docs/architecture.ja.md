# アーキテクチャ

[English](architecture.md) | 日本語

ExcelRenderer の内部構造、レイアウト Pass、拡張方法をまとめた資料です。インストールと利用方法は [README](../README.ja.md) を参照してください。

## 全体の処理フロー

### 描画前の任意のテンプレートマッピング

`ExcelRenderer.Mapping` は独立した .NET Standard 2.1 の NuGet パッケージです。

```text
XLSX テンプレート + C# オブジェクト / JSON
    -> ExcelTemplateMapper
    -> マッピング済み XLSX
    -> ExcelReader -> 既存の描画パイプライン
```

描画ライブラリやフォントを参照せずに XLSX の生成だけを行えます。
CLI は各パッケージを組み合わせ、`xlsx --data` ではマッピングして保存し、
描画コマンドの `--data` では一時ワークブックへマッピングしてから変換します。
マッピングはすべてのシートを検証し、その後に描画側のシート・範囲選択を適用します。
マッピングはレイアウト Pass を追加せず、描画モデルも変更しません。
導入手順・構文・制約は[マッピングガイド](mapping.ja.md)を参照してください。

### 描画

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

`ITextLayoutService`ではbaseline、runのX/advance、選択済み実face、明示的な実効サイズも確定します。
旧結果のサイズ未指定と明示0はページ倍率変換後も区別します。PDFsharpは選択済みデータに対応する内部face
キーを計測と描画で共有し、Skiaもメモリ上のフォントデータを使用します。確定下線は手動で一度だけ描き、
PDFフォント自身の下線属性は併用しません。

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

フォントマネージャー未指定時、共通Skia互換painterは指定family・weight・slantからシステムfaceを
一度だけ選択します。折返しやShrinkToFitによるサイズ変更後も、計測とtext/path描画は同じfaceを借用します。

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

## ソースコード構成

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
│  ├─ PdfSharpRenderer
│  ├─ PdfSharpTextMeasurer
│  └─ PdfSharpFontResolver
└─ SkiaSharp
   └─ PngRenderer
```

機能を追加する際は、既存クラスへ複数の責務を追加するのではなく、新しい読み込み処理、新しいレイアウト Pass、新しい描画コマンド、新しいレンダラー、または新しい抽象インターフェースとして分離することを基本方針とします。

## 確定テキストの拡縮と PDF フォントの寿命

`TextLayoutTransform` は確定テキストの幾何を破壊せずに拡縮する単一の処理です。全体寸法、行メトリクス、run 位置、明示された有効フォントサイズを同じ倍率で変換し、未指定の有効サイズは未指定のまま保持します。Shrink-to-fit は元サイズを先に確定し、ページ倍率は未指定状態を意図的に保持します。

PDFの確定faceは呼出し側のbytesをsnapshotし、face IDと内容digestで識別します。登録は不変bytesへの弱参照とし、元の解決済みfontがsnapshotを所有します。公開layoutは必要なfontを保持し、期限切れ登録は変換後に整理します。同じ有効faceの再利用では読込み・ハッシュを繰り返しません。本体/Coreはfont gateを共有します。[2エンジン構成](core-architecture.ja.md)を参照してください。

`FontManager` はフォントデータの同一性と解決結果の同一性を分けます。同じ byte 列を使う別名登録では face ID を共有できますが、解決結果は選択された登録と要求書体でキャッシュします。これにより、同じ登録の反復解決を再利用しつつ、登録上の family、weight、italic と近似診断を保持します。

## 確定レイアウト経路と互換文字描画経路

PDFsharp と Skia では、文字の振り分けを図形・罫線・画像の描画から分離しています。各バックエンドの
`TextPainter` が縦書き正規化、ページ座標のクリップ、回転、Save/Restore の均衡を所有し、その後に
経路を一つだけ選択します。確定レイアウト経路は保存済みの baseline、run の X 位置、実効サイズ、
解決済み face を使用し、再折返しや再縮小を行いません。レイアウトを持たないコマンドは互換経路へ進み、
従来の折返し、ShrinkToFit、フォールバック、IVS、絵文字処理を維持します。バックエンド固有オブジェクトは
各名前空間内に閉じ、共通の配置・実効サイズ helper は PDFsharp/Skia に依存しません。

## ページ計画

`PaginationPass` は全計算を抱えず、ページ工程を組み立てます。`PageBandBuilder` は一軸の半開区間の帯を作り、
`PrintScaleResolver` の FitToPages 評価と本番ページ生成が同じ計算を利用します。`PagePlacement` はページごとの
余白、中央配置、反復タイトル offset、本文 clip、セルと本文オブジェクトで異なる座標変換を確定します。
`HeaderFooterLayout` は最終ページ番号・総ページ数が決まった後にフィールドを展開します。これらは不変な値を
入力として受け取り `ReportLayoutContext` を変更しません。`RenderPageBuilder` は1ページ分のセルとオブジェクトを
選択・変換し、画像・図形の anchor を一度だけ解決します。回転後の視覚範囲は所属判定だけに使い、描画 Bounds は
元の範囲を維持します。`PaginationPass` には印刷対象の準備、ページ順の選択、ページ生成、ヘッダー付与が残ります。

### 明示選択と最終出力座標

`SelectionOptions.Ranges` は選択済みシートとcheckedな総セル数でSink Open前に検証します。元セルの削除や結合span変更をせず、要求ごとに印刷設定だけを複製します。`ExplicitRangeGeometryPass` は交差する結合セルの元寸法を維持し、明示選択の可視行列は元シート座標を使います。`RenderPageBuilder` は本文／反復タイトル別の `PageSourceRegion` を記録し、連続の明示選択は固定矩形を維持します。

描画コマンドは確定文字レイアウトを保持し、セルごとのclipと元の回転オブジェクトのclipを適用します。`DrawCommandBounds` がclip後の可視境界をunionし、`PageViewport` が確定シーンを平行移動してPDF／PNG／SVGとdescriptorへ最終寸法を供給します。viewportのSaveはfinallyでRestoreし、PNG上限は事前検証します。元セル領域・要求範囲・元寸法・crop・paddingはSchema 1の任意metadataです。

`HyperlinkReader` はXML定義・relationship・名前スコープ・literal HYPERLINKを読み、リンク先の取得や数式評価をしません。問題は元定義に保持し、PDF／Markdownでリンク元が出力される場合に確定します。`HyperlinkPolicy` はURI方針と単純内部参照解決を共用します。PDFは選択後の最終ページと元領域で解決し、`PdfHyperlinkWriter` はImport完了後の最終文書に注釈を追加して、上端基準ptをPDF座標に変換します。Markdownはplain textとリンク先を分離し、全表示経路で共通formatterを使用し、必要な安定アンカーと空／範囲リンクの明示一覧を出力します。Noneと画像出力ではリンク診断を評価しません。
