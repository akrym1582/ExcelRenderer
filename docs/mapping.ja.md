# Excel テンプレートへのマッピング

[English](mapping.md) | 日本語

`ExcelRenderer.Mapping` は独立した .NET Standard 2.1 のパッケージです。
ClosedXML 0.105.1 を使い、C# オブジェクトや JSON の値を XLSX テンプレートへ設定します。
描画ライブラリ、フォント、PDFsharp、SkiaSharp には依存しません。
CLI はマッピングと描画のライブラリを組み合わせて使用します。

レイアウトは Excel で作成し、値はアプリケーションから渡せます。
生成したファイルは通常の XLSX なので、Excel で開くことも、ExcelRenderer で
PDF・PNG・SVG・Markdown へ変換することもできます。

初回の手順は[最初のワークブック](#最初のワークブック請求書)、
テンプレートの設計には[仕様一覧](#仕様一覧)、
[配列ブロック](#複数行入れ子の配列展開)、[改ページ](#改ページ)を参照してください。

## インストール

C# から XLSX の生成だけを行う場合は、利用先のプロジェクトディレクトリで実行します。

```sh
dotnet add package ExcelRenderer.Mapping
```

.NET 8・.NET 10 など、.NET Standard 2.1 と互換性のあるフレームワークを指定してください。
.NET Framework には対応していません。マッピングライブラリにはフォント、
ExcelRenderer、Microsoft Excel のインストールは不要です。

コマンドラインで利用する場合は、.NET 10 SDK をインストールしてからツールを追加します。

```sh
dotnet tool install --global ExcelRenderer.Tool
excelrenderer xlsx --help
```

インストール済みの場合は `dotnet tool update --global ExcelRenderer.Tool` で更新します。
CLI はマッピング・描画・同梱フォントを含みます。CLI の利用だけなら
`ExcelRenderer.Mapping` を個別にインストールする必要はありません。

これらのコマンドは NuGet.org がパッケージソースとして有効であり、マッピングを含む
バージョンが公開済みであることを前提とします。公開前に試す場合は
[ローカルパッケージの導入手順](../README.ja.md#開発)を参照してください。

## 最初のワークブック：請求書

### 1. テンプレートを作成する

Excel などの XLSX エディターで空のワークブックを作成し、シート名を `Invoice` にします。
次の内容を入力し、作業用ディレクトリへ `template.xlsx` として保存してください。

| 行 | A 列 | B 列 | C 列 | D 列 |
| --- | --- | --- | --- | --- |
| 1 | Invoice | | | |
| 2 | Customer | `**Customer` | | |
| 3 | Issued | `**IssuedAt \| date("yyyy/MM/dd")` | | |
| 4 | Item | Quantity | Unit price | Line total |
| 5 | `**Items[*].Name` | `**Items[*].Quantity` | `**Items[*].Price` | `**Items[*].LineTotal` |
| 6 | Total | | | `**Total` |

`**` で始まる式は、先頭に `=` を付けず、セルの文字列として入力します。
D5 には入力側で計算した明細金額を渡します。繰り返す行内の数式は拒否されます。
Markdown の表にあるパイプ前のバックスラッシュは
表の表示用です。B3 に入力する内容は `**IssuedAt | date("yyyy/MM/dd")` です。
5 行目には Excel の「テーブル」を使わず、通常のセルを使ってください。
フォント、列幅、罫線、表示形式は Excel 側で設定できます。この例では
C5・D5・D6 の表示形式を `#,##0.00`、印刷範囲を A1:D6 にします。
XLSX エディターがない場合は、後述の
[C# によるテンプレート生成](#excel-を使わずにサンプルテンプレートを作成する)を使えます。

### 2. データを用意する

`template.xlsx` と同じディレクトリに、次の内容を `data.json` として保存します。

```json
{
  "Customer": "Alice",
  "IssuedAt": "2026-10-09",
  "Items": [
    { "Name": "Book", "Quantity": 2, "Price": 12.50, "LineTotal": 25.00 },
    { "Name": "Pen", "Quantity": 3, "Price": 2.00, "LineTotal": 6.00 }
  ],
  "Total": 31.00
}
```

キーの名前は大文字・小文字も含めてテンプレートと一致させます。
この例では、行挿入で数式の範囲が伸びることに依存しないよう、合計 `Total` も
データ側で用意しています。

### 3. XLSX を生成して確認する

2 つのファイルを置いたディレクトリで実行します。

```sh
excelrenderer xlsx template.xlsx --data data.json -o report.xlsx
```

`report.xlsx` を開くと、B2 は `Alice`、B3 は `2026/10/09` になり、
5 行目が 2 行の商品明細へ展開されます。

| 行 | 商品 | 数量 | 単価 | 明細金額 |
| --- | --- | --- | --- | --- |
| 5 | Book | 2 | 12.50 | 25.00 |
| 6 | Pen | 3 | 2.00 | 6.00 |
| 7 | Total | | | 31.00 |

D6 には2件目の `LineTotal` が入ります。行の書式や合計行も展開に追従します。
`template.xlsx` と `data.json` は次回の入力として使えます。
`xlsx` は成功時に既存の出力 XLSX を上書きします。以前の結果を残す場合は
別の出力先を指定してください。マッピングのエラーは出力先を開く前に検出します。

### 4. 必要に応じて描画する

確認した XLSX を PDF に変換できます。

```sh
excelrenderer pdf report.xlsx -o report.pdf
```

テンプレートからマッピングと描画を 1 コマンドで実行することもできます。

```sh
excelrenderer pdf template.xlsx --data data.json -o report.pdf
excelrenderer svg template.xlsx --data data.json -o ./svg-output
excelrenderer image template.xlsx --data data.json -o ./png-output
excelrenderer markdown template.xlsx --data data.json -o report.md
excelrenderer render template.xlsx --data data.json --format png -o ./png-output
```

使いたいコマンドを 1 つずつ選び、未使用の描画出力先を指定してください。
描画コマンドは既存の出力ファイルや空でない出力ディレクトリへの出力を拒否します。

## CLI リファレンス

| コマンド | 出力先 | データ指定 |
| --- | --- | --- |
| `xlsx` | XLSX ファイル | `--data <data.json>` が必須 |
| `pdf` | PDF ファイル | `--data <data.json>` は任意 |
| `image` / `svg` | 出力ディレクトリ | `--data <data.json>` は任意 |
| `markdown` / `md` | Markdown ファイル | `--data <data.json>` は任意 |
| `render` | PDF ファイル、または PNG/SVG/Markdown のディレクトリ | `--data <data.json>` は任意。`--format` は必須 |

`xlsx` は `--data` が必須で、マッピングと保存だけを行います。
描画コマンドで `--data` を省略すると、従来どおり既存のワークブックを変換します。
指定時は描画前にマッピングします。一時ワークブックは元の入力ファイル名を維持し、
成功・失敗のどちらでも削除します。入力 XLSX や JSON と同じパスは出力先に指定できません。
マッピングのエラーは出力先を開く前に検出します。

マッピングはすべてのワークシートを処理します。`--sheet` で描画対象を 1 シートに
絞っても、ほかのシートの式も検証・評価します。空白を含むパスは引用符で囲んでください。

## C# API

以下は前述の請求書テンプレートを使う例です。`template.xlsx` と `data.json` を
置いたディレクトリで実行します。新しいアプリケーションで試す場合は、
`dotnet new console -n MappingDemo` で作成し、そのディレクトリへ移動して
`ExcelRenderer.Mapping` を追加します。`Program.cs` をいずれかの例に置き換え、
入力ファイルを同じディレクトリへコピーして `dotnet run` を実行してください。

### C# オブジェクトをマッピングする

```csharp
using System;
using ExcelRenderer.Mapping;

ExcelTemplateMapper.Map("template.xlsx", "report.xlsx", new
{
    Customer = "Alice",
    IssuedAt = new DateTime(2026, 10, 9),
    Items = new[]
    {
        new { Name = "Book", Quantity = 2, Price = 12.50m, LineTotal = 25.00m },
        new { Name = "Pen", Quantity = 3, Price = 2.00m, LineTotal = 6.00m },
    },
    Total = 31.00m,
});
```

CLR データでは public な読み取り可能プロパティや辞書のキーを使用します。
名前は大文字・小文字を区別します。JSON へ変換せず、元の CLR 型を維持します。
`Items` にはアプリケーションの列挙可能なコレクションも渡せます。

### JSON ファイルをマッピングする

ファイル間のマッピングには、`JsonElement` を受け取る `Map` を使います。

```csharp
using System.IO;
using System.Text.Json;
using ExcelRenderer.Mapping;

using var document = JsonDocument.Parse(File.ReadAllText("data.json"));
ExcelTemplateMapper.Map("template.xlsx", "report.xlsx", document.RootElement);
```

マッピング終了まで、所有元の `JsonDocument` を破棄しないでください。
`MapJson` は JSON のファイルパスではなく **JSON の文字列**を受け取り、ストリームを使用します。

```csharp
using System.IO;
using ExcelRenderer.Mapping;

using var template = File.OpenRead("template.xlsx");
using var mapped = new MemoryStream();
ExcelTemplateMapper.MapJson(template, mapped, File.ReadAllText("data.json"));
mapped.Position = 0;
using var output = File.Create("report.xlsx");
mapped.CopyTo(output);
```

マッパーは入力・出力ストリームを閉じません。出力ストリームの現在位置から書き込むため、
新しいワークブックには空のストリームを使ってください。マッピング結果を読み込む場合や
`ExcelConverter.RenderAsync` へ渡す場合は、位置を先頭へ戻します。

### C# から描画する

アプリケーションに `ExcelRenderer` を追加し、必要なら日本語・絵文字の同梱フォント用に
`ExcelRenderer.Fonts` も追加します。マッピングだけなら、どちらも不要です。
`report.xlsx` を生成した後、次のように描画できます。

```csharp
using ExcelRenderer;

await ExcelConverter.ConvertToPdfAsync("report.xlsx", "report.pdf");
```

描画には適切なフォントが必要です。[フォントの導入手順](../README.ja.md#フォント)も参照してください。

### オプションとエラー

`MappingOptions.Culture` の既定値は `InvariantCulture` です。
`MaxOutputRows` の既定値はシートごとに 100,000 行で、Excel の上限である
1,048,576 行まで設定できます。入れ子の展開と通常のテンプレート行も数えます。
キャンセルは計画・展開中に確認しますが、ClosedXML の同期的な読み込み・保存処理は
途中で中断できません。ワークブックと展開計画はメモリ上に保持します。
CLI では既定値を使用します。変更が必要な場合は C# API を使ってください。

```csharp
using System.Globalization;
using System.IO;
using System.Text.Json;
using ExcelRenderer.Mapping;

using var document = JsonDocument.Parse(File.ReadAllText("data.json"));
var options = new MappingOptions
{
    Culture = CultureInfo.GetCultureInfo("ja-JP"),
    MaxOutputRows = 200_000,
};
try
{
    ExcelTemplateMapper.Map("template.xlsx", "report.xlsx", document.RootElement, options);
}
catch (MappingException error)
{
    Console.Error.WriteLine($"{error.SheetName}!{error.CellAddress}: {error.Message}");
}
```

エラーのセル位置は、入れ子の展開後でも **元のテンプレート**の位置です。
不正な JSON やファイルアクセスの失敗は、`MappingException` とは別の例外になります。
ファイル API は出力先を開く前に検証・生成するため、マッピングのエラー時は
既存の出力ファイルを維持します。

## 仕様一覧

| セルの文字列 | 処理 | 配置条件 |
| --- | --- | --- |
| `**Customer.Name` | セル全体をスカラー値に置き換え | 文字列セル |
| `**$.Customer.Name` | 明示的にルートから参照 | 文字列セル |
| `**Items[0].Name` | 配列の 1 要素を参照。インデックスは 0 始まり | 文字列セル |
| `**Items[*].Name` | 配列の要素ごとに行全体を繰り返す | 明示的な配列ブロックの外側 |
| `**@start-array Items[*] as item` | 複数行の繰り返しブロックを開始 | 内容セルが 1 つだけの、結合のない専用行 |
| `**@item.Name` | エイリアスに結び付いた要素を参照 | そのブロック内、または入れ子の子ブロック内 |
| `**@end-array` | 対応するブロックを終了 | 内容セルが 1 つだけの、結合のない専用行 |
| `**Price \| format("N2")` | 書式を適用して文字列化 | スカラー式・ワイルドカード式 |
| `**IssuedAt \| date("yyyy/MM/dd")` | 日付を明示的に変換・書式化して文字列化 | スカラー式・ワイルドカード式 |
| `**@page-break` | セルを空にして、展開後の位置の上・左に改ページを追加 | 通常行。繰り返し領域の中にも配置可能 |
| `\**literal` | 先頭のバックスラッシュを 1 つ除き、文字列として出力 | 文字列セル |

### 式の認識と評価

`**` または `\**` で始まる文字列セルだけを解釈します。
式の前に空白や Excel の `=` を付けないでください。数式セル・数値セル・通常の文字列は
ワークブックの内容として維持します。セル全体を置き換えるため、
`Customer: **Customer` のような文字列への埋め込みには対応しません。

ディレクティブ名は大文字・小文字を区別します。
`**@end-array` と `**@page-break` は、末尾の空白や引数を付けず、そのまま入力します。
`**@page-break` に方向・条件・ページ数を指定する引数はありません。
書式指定は `format()` または `date()` の 1 つで、引数は 1～2 個の JSON 文字列です。
関数の連結には対応しません。算術・条件分岐・計算は入力データ側や通常の Excel 数式で行います。

マッピングでは全シートの構造とパス構文を検証し、実際に出力する行を評価した後で展開します。
描画対象の選択とは独立しています。要素がないブロック内の式は構文検証だけを行い、
データのパスは評価しません。

### 存在しない値・null・空配列

| 入力の状態 | `**Customer` などのスカラーセル | `**Items[*].Name` などの繰り返し |
| --- | --- | --- |
| プロパティが存在しない | 元テンプレートのセル位置付きのエラー | 元テンプレートのセル位置付きのエラー |
| 明示的な `null` | 空セル | 配列が必要なためエラー |
| 空配列 `[]` | 配列はスカラー値ではないためエラー | 行、または明示的なブロック全体を削除 |
| オブジェクト・空でない配列 | スカラーのプロパティやインデックスを選ぶ必要があり、エラー | 配列は要素順に繰り返す。配列でないオブジェクトはエラー |

配列要素の中に参照先のプロパティがない場合もエラーです。
null の要素は `**Items[*]` のようなスカラーパスなら空セルになりますが、
その null からプロパティを参照すると失敗します。
パスにはオプショナルな参照や既定値を指定する演算子はありません。

## セルの式

`**` で始まる文字列セルは、セル全体を値に置き換えます。繰り返し外の数式セルは数式のままです。
`Name: **name` のような埋め込み式は、通常の文字列として残ります。

| テンプレートの文字列 | 意味 |
| --- | --- |
| `**$.name.value` | ルートオブジェクトから参照 |
| `**name.value` または `**.name.value` | 同じルートパス |
| `**items[0].name` | 0 以上の配列インデックスを参照 |
| `**$['a.b']["key\|name"]` | 記号を含むキーをそのまま参照 |
| `\**literal` | リテラル文字列 `**literal` を出力 |
| `**$` | スカラーのルート値を設定 |

対応する JSONPath 構文は、プロパティ参照、引用符付きの角括弧キー、
0 以上の配列インデックス、`[*]` です。角括弧キーでは、同じ種類の引用符と
バックスラッシュをエスケープできます。フィルター、再帰下降、スライス、ユニオン、
1 つのパス内の複数ワイルドカードには対応していません。入れ子の配列には専用ブロックを使います。

存在しないプロパティやインデックスはエラーです。明示的な JSON/CLR の null は空セルになります。
オブジェクトや配列全体をスカラーセルへ設定することはできません。
直接設定できない CLR 型でも、`IFormattable` を実装していれば `format()` を使えます。
数値・真偽値・`DateTime`・`TimeSpan` は Excel の型を維持します。
Excel の数値は浮動小数点なので、大きな整数や高精度の decimal は精度制限を受けます。
文字列表現を維持したい場合は書式指定を使ってください。

## 書式指定

```text
**price | format("N2")
**price | format("C0", "ja-JP")
**items[*].code | format("0000")
**issuedAt | date("yyyy/MM/dd")
```

`format(format[, culture])` は値の CLR 型の `IFormattable.ToString` を呼び出します。
省略可能な culture は `MappingOptions.Culture` を上書きします。
引数は JSON の文字列リテラルです。引用符付きキー内のパイプは式の区切りになりません。
書式指定後は Excel の **文字列セル**になります。数式に使う数値を維持するには、
`format()` を付けず、テンプレート側の Excel の表示形式を設定します。

`date(format[, culture])` は CLR の `DateTime`・`DateTimeOffset` を書式化するか、
明示的に指定された場合だけ ISO 8601 の JSON 文字列を変換します。
受け付ける形式は `yyyy-MM-dd` または `yyyy-MM-ddTHH:mm:ss` で、
1～7 桁の小数秒と `Z` または `±HH:mm` のオフセットを任意で付けられます。
`date()` のない文字列は文字列のままです。実行環境のカルチャーで日付を推測しません。
不正な値や非対応の書式はエラーになり、null は空セルのままです。

## 1 行の配列展開

たとえば、次のセルを同じ行に置きます。

```text
A5: **items[*].name
B5: **$.items[*].quantity
C5: 固定の文字列
D5: **title
```

固定セルやルートパスも含め、行全体を要素数だけ繰り返します。
同じ行のワイルドカードセルは、同じ正規化済み配列パスを参照する必要があります。
空配列はその行を削除します。別々の行は独立した繰り返しになります。

## 複数行・入れ子の配列展開

開始・終了マーカーは、内容のあるセルが 1 つだけの専用行に置きます。
マーカー行に結合セルは使えません。開始・終了の両方の行は出力から消えます。

```text
**@start-array $.orders[*] as order
  **@order.number
  **@start-array @order.lines[*] as line
    **@line.name
    **@line.quantity
    **@order.number
  **@end-array
  **@order.total
**@end-array
```

上のインデントは入れ子の説明用で、各行は Excel の 1 行を表します。
実際のマーカーや式の前には空白を入力しないでください。
各要素について、対応する開始・終了マーカーの間の全行を複製します。
空配列はブロック全体を削除します。エイリアス名は必須で、外側のエイリアスを
上書きできません。独立した兄弟ブロックでは同じ名前を再利用できます。
エイリアスはそのブロックと入れ子の子ブロックでのみ使えます。
`@order` はその要素を参照し、`$` やルート指定を省略したパスは常にルートを参照します。
エイリアスパスはマッパー独自の JSONPath 拡張です。

ブロック内では、1 行のワイルドカード展開ではなく明示的な入れ子のマーカーを使います。
開始パスは `[*]` で終わる必要があります。マーカーの不一致、未知のエイリアス、
非対応の構文は配列が空でも検証します。空配列で実体化されないブロック内の
データの有無は評価しません。

## 改ページ

`**@page-break` はセルを空にし、その行の直上と列の直左に改ページを入れます。
C10 なら 9 行目の後と B 列の後です。1 行目や A 列の前には改ページを入れません。
セルの行は出力に残ります。繰り返し内の指定は各展開位置で適用し、重複はまとめます。

### 方向に合わせたセルの選び方

| 指定セル | 行方向の改ページ | 列方向の改ページ |
| --- | --- | --- |
| A10 | 9 行目の後 | なし |
| C1 | なし | B 列の後 |
| C10 | 9 行目の後 | B 列の後 |
| A1 | なし | なし |

行方向だけに改ページする場合は A 列のセルを確保します。列方向だけなら 1 行目に置きます。
シートの内側のセルに置くと **両方向**に改ページします。
行専用・列専用の別のディレクティブ名はありません。
改ページはセルや配列ブロックの周囲だけではなく、印刷範囲全体を行・列の境界で分割します。

開始・終了マーカーとは異なり、改ページに専用行は不要です。同じ行のほかのセルに
データや数式を置けます。指定セルは空になりますが、その書式と行は残ります。
文字列としてマーカーを表示する場合は `\**@page-break` と入力します。この場合は改ページしません。

### 例：印刷範囲を 4 ページに分割する

小さなテンプレートに次の内容を置き、印刷範囲を A1:D3 にします。
各領域が倍率 100% で用紙に収まるようにしてください。

```text
A1: Upper left
D1: Upper right
C2: **@page-break
A3: Lower left
D3: Lower right
```

空の JSON オブジェクト `{}` でマッピングすると、1 行目の後と B 列の後に改ページが入ります。
印刷範囲が上下 2 分割・左右 2 分割になるため、ページ単位の PDF・SVG・PNG では
4 ページになります。用紙サイズ・余白・倍率・内容の大きさによっては、
自動改ページでさらにページが増えます。

### 例：繰り返す各要素を新しいページから始める

```text
A1: **@start-array Items[*] as item
A2: **@page-break
B2: **@item.Name
A3: **@end-array
```

開始・終了の行は削除されます。3 要素なら内容の行は 1・2・3 行目になります。
最初の指定は A1 なので先頭行の前には改ページを入れず、
残りの指定によって 1・2 行目の後に改ページが入ります。
指定セルが A 列なので列方向の改ページは入りません。

位置は **展開後**に決まります。配列の下にある指定は、上で挿入・削除された行に追従します。
空配列内の指定はブロックとともに消え、改ページも印刷モードの変更も行いません。

### 印刷設定と出力への影響

既存の手動行改ページは複製した行に追従し、列改ページは維持します。
改ページ指定を含むシートは「ページ数に合わせる」設定から倍率指定へ切り替えます。
正の倍率があれば維持し、なければ 100% にします。Excel と SVG/PNG/PDF の描画で
手動改ページを有効にするためです。ほかのシートの印刷モードは維持します。
出力に残る指定が A1 の場合も、改ページは追加しませんが倍率指定へ切り替えます。
もともと 1 ページに収める設定だったテンプレートは、マッピング後にページ数が増えることがあります。

XLSX には印刷・印刷プレビュー用の手動改ページとして保存します。
ワークブックを別々のシートへ分割する機能ではありません。ページ単位の描画はこの改ページを使います。
連続画像や Markdown が、この指定によってページ単位の出力へ変わることはありません。

## Excel 機能と制約

値・書式だけの領域は最終行配置を計算し、領域ごとにまとめて行数を変更します。
参照やその他の Excel オブジェクトがある場合は、既存の動作を維持するため
ClosedXML の行挿入・削除と範囲コピーを使います。
セルの書式、行高、非表示状態、アウトラインレベル、通常の結合セル、
コピー先の条件付き書式とデータ検証を維持します。
既存の数式参照、定義名、印刷範囲は ClosedXML の行操作に従います。
繰り返し領域の境界で終わる範囲は、すべての複製行へ伸びるとは限りません。
合計の範囲を適切に設計するか、入力データで合計を渡してください。
削除したマーカー行や空配列行への参照は `#REF!` になることがあります。

繰り返す行・ブロック内の数式と、そこに重なる範囲を参照する定義名は非対応です。
空配列でも入力データの評価・出力前に拒否します。これは互換性の変更です。
明細の計算値は C# / JSON 側で渡してください。定義名はワークブック・シートの
両スコープを検査し、マーカー行も含む行帯の全列を対象にします。
外側の数式・定義名は、繰り返しより下側も含めて上記の参照制約の範囲で維持します。
印刷範囲・印刷タイトルはページ設定として維持します。

繰り返しブロック内で完結する結合は使えます。配列境界をまたぐ結合、
結合されたマーカー行、繰り返し領域と重なる Excel テーブル・画像は、
元テンプレートのセル位置を示すエラーになります。
その他のワークブック機能は ClosedXML の読み込み・保存の対応範囲に依存します。
繰り返し領域には通常のセルを使ってください。

対応する数式は保存前に評価し、元の数式を維持して Excel の自動再計算を要求します。
非対応の関数はキャッシュに Excel のエラー値が入る場合があるため、直後の描画には
利用できません。その値は C# や JSON 側で計算して渡してください。

`MappingException` の `SheetName`・`CellAddress`・`Expression` は、
入れ子の展開後でも元のテンプレートを示します。

## Excel を使わずにサンプルテンプレートを作成する

同じ請求書テンプレートを C# で生成することもできます。
`ExcelRenderer.Mapping` を追加したコンソールプロジェクトで、
`Program.cs` を次のコードに置き換え、`dotnet run` を 1 回実行してください。
ClosedXML はマッピングパッケージの依存関係として導入されます。
その後、`Program.cs` を前述のマッピング例に置き換えるか、CLI を使います。

```csharp
using ClosedXML.Excel;

using var workbook = new XLWorkbook();
var sheet = workbook.AddWorksheet("Invoice");
sheet.Cell("A1").Value = "Invoice";
sheet.Cell("A2").Value = "Customer";
sheet.Cell("B2").Value = "**Customer";
sheet.Cell("A3").Value = "Issued";
sheet.Cell("B3").Value = "**IssuedAt | date(\"yyyy/MM/dd\")";
sheet.Cell("A4").Value = "Item";
sheet.Cell("B4").Value = "Quantity";
sheet.Cell("C4").Value = "Unit price";
sheet.Cell("D4").Value = "Line total";
sheet.Cell("A5").Value = "**Items[*].Name";
sheet.Cell("B5").Value = "**Items[*].Quantity";
sheet.Cell("C5").Value = "**Items[*].Price";
sheet.Cell("D5").Value = "**Items[*].LineTotal";
sheet.Cell("A6").Value = "Total";
sheet.Cell("D6").Value = "**Total";
sheet.Columns(1, 4).Width = 22;
sheet.Range("A4:D4").Style.Font.Bold = true;
sheet.Range("C5:D6").Style.NumberFormat.Format = "#,##0.00";
sheet.PageSetup.PrintAreas.Add("A1:D6");
workbook.SaveAs("template.xlsx");
```

## よくある問題と対処

| 症状 | 確認すること |
| --- | --- |
| NuGet で `ExcelRenderer.Mapping` が見つからない | NuGet.org を有効にし、マッピングを含むリリースが公開済みか確認します。公開前はインストール節のリンクにあるローカルパッケージを使います。 |
| `xlsx` や `--data` が認識されない | `ExcelRenderer.Tool` をマッピング対応の公開済みバージョンへ更新します。`excelrenderer xlsx --help` で確認できます。 |
| 式が文字列のまま残る | 文字列セルの先頭が `**` である必要があります。`Customer: **Customer` は通常の文字列です。ラベルと式を別セルにします。 |
| `Property '…' was not found` | プロパティ名の大文字・小文字を一致させます。欠落や範囲外のインデックスはエラーで、明示的な `null` は空セルです。 |
| スカラー値が必要というエラー | `**Customer.Name` のようなプロパティ、インデックス、配列展開を使います。オブジェクトや配列全体はスカラーセルに入れられません。 |
| `format()` が失敗する／数値が数式で使えない | 書式指定は `IFormattable` の値を文字列にします。数式に使う値には Excel の表示形式を設定します。JSON の数値には、整数専用の CLR 書式 `D4` などではなく `0000`・`N2` などを使います。 |
| 日付が ISO 文字列のまま | `date("yyyy/MM/dd")` を明示します。文字列は日付へ自動変換されません。 |
| 繰り返し行が消える | 空配列は行やブロックを削除します。データ行が必要なら少なくとも 1 要素を渡します。 |
| マーカーの専用行／エイリアスのエラー | 開始・終了をそれぞれ結合のない専用行に置き、対応する組にします。エイリアスはそのブロック内で使います。 |
| `MaxOutputRows` を超える | データを減らすか、C# で `MappingOptions.MaxOutputRows` を設定します。通常の行も数えます。CLI の既定値は 100,000 行です。 |
| 合計が最初の要素だけ／数式が `#REF!` になる | 範囲は ClosedXML の行操作に従います。合計は入力で渡し、マーカー行や空配列で削除する行への参照を避けます。 |
| 描画結果の数式が `#NAME?` | ClosedXML はすべての Excel 関数を評価できません。入力側で値を計算します。直後の描画では、Excel を開いた後の再計算は使えません。 |
| 描画先が既に存在するというエラー | 新しいファイルや空のディレクトリを指定します。描画とは異なり、成功時の `xlsx` マッピングは既存の出力 XLSX を上書きします。 |
| 描画対象外のシートでマッピングエラー | 描画対象を選ぶ前に全シートを評価します。式を修正するか、そのシートをテンプレートから除きます。 |
