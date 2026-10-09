# ExcelRenderer Core

XLSXからPDFだけを生成する独立したソースプロジェクトです。`netstandard2.1`で、.NET 8/10からProjectReferenceできます。NuGetパッケージ、CLI、フォント入りDLLは作成しません。

公開型名はオリジナル版と同じ `ExcelConverter`、`PdfExportOptions`、`WorkbookInputOptions`、`ConversionResult`、`ConversionDiagnostic` です。名前空間は `ExcelRenderer.Core` で、設定や結果のメンバーはCore版の対応範囲に合わせています。従来の `Core` 接頭辞付きの型名を使用している呼び出し側は、これらの名前へ変更してください。

```xml
<ItemGroup>
  <ProjectReference Include="../ExcelRenderer/src/ExcelRenderer.Core/ExcelRenderer.Core.csproj" />
</ItemGroup>
```

```csharp
using ExcelRenderer.Core;

using var xlsx = File.OpenRead("report.xlsx");
using var pdf = new FileStream("report.pdf", FileMode.Create, FileAccess.ReadWrite);
var result = await ExcelConverter.ConvertAsync(xlsx, pdf, new PdfExportOptions
{
    FontFilePath = "/fonts/NotoSansJP-Regular.ttf",
    SheetName = null,
});
Console.WriteLine(result.PageCount);
```

## フォントと表示

呼出し側が指定する静的TrueTypeアウトラインの単一sfntを使います。拡張子では判定しません。テーブル範囲・必須テーブル・可変/CFF形式を検証し、SkiaSharpとPDFsharpで読込みを確認してからページを生成します。TTC/OTC、CFF OTF、可変フォント、WOFF/TTEは対象外です。破損・未対応形式は`InvalidDataException`、不存在は`FileNotFoundException`です。

フォントbytesは変換ごとに一度読み、同じsnapshotで列幅・行高・文字幅・PDF描画を処理します。セルとヘッダー/フッターは通常書体に統一します。太字・斜体の疑似描画、フォント探索・fallback、IVSの異体字形の再現、外字・カラー絵文字の特別処理はありません。セルとヘッダー/フッターの異体字セレクター（U+FE00–U+FE0F、U+E0100–U+E01EF）は計測・描画前に除去し、基底文字を表示します。例えば「葛」＋IVSは「葛」として表示し、セレクター由来の追加の豆腐や文字幅は生成しません。基底文字自体が指定フォントに未収録の場合の表示は保証しません。サイズ・色・下線を維持します。未収録文字は指定フォント/PDFsharpの通常動作に従い、すべてのUnicode文字の表示は保証しません。フォント置換によりExcel原本と列幅や折返しが変わることがあります。

## 印刷と画像

nullのSheetNameは非表示状態でも追加フィルターせず、ブック順に全シートを出力します。単一SheetNameを指定でき、不存在は`ArgumentException`です。空シートは1ページです。Excel内の単一/複数印刷範囲、用紙、余白、倍率、FitToPages、ページ順、繰返し行列、改ページ、結合セル、背景、罫線、配置、折返し、ShrinkToFit、ヘッダー/フッターを維持します。ヘッダー日時は1変換に1つのsnapshotです。セル文字の回転は無視します。

画像は元画像全体を配置枠へ伸縮し、位置・サイズ・Z順を維持します。開始点が所属する印刷領域のページにだけ出力し、はみ出しはPDFページ境界で切れます。開始点が印刷範囲外なら省略します。後続ページへの複製・分割、繰返し行列由来の画像複製、crop・回転・反転を行いません。同じ開始点を含む複数印刷範囲では領域ごとに印刷します。

図形・吹き出し・図形内文字・クリック可能なPDFリンクは生成しません。リンク付きセルの表示文字とHYPERLINK式のCachedValueは保持します。一般セルは既存と同じGetFormattedString方針で、数式エンジンは追加せず、Excel再計算との同一結果は保証しません。仕様上無視する図形や画像加工ごとの大量警告は作りません。画像decode失敗などの非致命的な診断はresult.Diagnosticsで取得できます。

APIのセル範囲、ページ抽出、trim、連続画像、PNG/SVG/Markdownはありません。

## Stream・資源契約

入力は現在位置から読み、seekable/nonseekableに対応します。出力は書込み可能・seekable・開始位置0・長さ0が必須です。呼出し元の両streamを閉じません。非seekable出力向けの全PDFbufferはありません。キャンセルはOperationCanceledExceptionです。読込み・ページ境界・Saveの書込み境界で確認します。同期ライブラリ処理を瞬時には中断できません。

入力既定値はメモリ16MiB、最大128MiB、ZIP entry 10,000、展開合計512MiB、一時ファイル不許可です。許可したspoolのtempは成功・失敗で削除します。Save時のI/O失敗やキャンセルで途中データが残る場合があり、API全体の原子的書込みは保証しません。[sample](../samples/ExcelRenderer.Core.Sample/Program.cs)は同じディレクトリの一時ファイルへ書き、streamを閉じてから目的ファイルへ移動し、失敗時は一時ファイルを削除します。

```bash
dotnet run --project samples/ExcelRenderer.Core.Sample -c Release -- input.xlsx output.pdf /fonts/font.ttf
```

1ページのpayloadだけを生成して最終PdfDocumentへAppendPageし、Saveは1回です。文字layout cacheは512件・文字列2048まで、画像cacheの推定decoded資源上限は64MiBです。画像leaseとnative資源は変換内で解放します。ただしClosedXMLモデルと最終PDF文書は保持され、PDFsharpが文書に保持する画像はcache上限とは別です。定メモリやRSS削減率は保証しません。SkiaSharp/native DLLは画像decode・行高・列幅の計測に必要です。

Core内のSemaphoreSlimでPDFsharpフォントresetから計測・描画・Save・文書解放まで直列化します。待機はキャンセル可能です。異なるフォントのCore呼出しは連続・並行で利用できます。同じCore DLL/load contextを使う通常版との同一process混在は共有gateで同期します。他ライブラリのPDFsharp操作や別AssemblyLoadContextは対象外です。旧版との比較は別processで実施します。

## ソースと保守

取り込み元はmain 1.7.2、コミット`32e9165d145516dbd0bcb4c2ae31a78ce6774bce`です。[ファイル対応表](core-source-map.md)に選択ソースの移植元・移植先を記録しています。既存本体・Fonts・Toolへの参照、既存ソースのCompile Include、wrapperはありません。共通処理の修正はCoreへ行い、本体とCoreの方針差を両方の回帰テストで確認します。依存versionは基準版と同じです。テストにコピーするNoto素材は既存OFLと著作権表示を維持し、Core本体には埋め込みません。

## 検証

[検証結果と再現手順](core-validation.md)を参照してください。Windows CIの結果は、このLinux環境の実行結果とは区別します。

ファイルの対応・所有者・製品ごとの差分は[所有者表](core-source-map.md)を参照してください。

ヘッダー/フッターは既存のフィールド置換・配置を継承します。Excel独自の書式制御コードを新たに解析する処理は追加していません。LinuxのSkia native層は初期化時にfontconfigへアクセスする場合があります。指定フォント以外のfaceは選択しませんが、native層の初期化アクセスが皆無とは主張しません。

## 改名と共通化の状態

旧 `ExcelRenderer.Slim` の利用側はProjectReferenceを `src/ExcelRenderer.Core/ExcelRenderer.Core.csproj` へ、usingを `ExcelRenderer.Core` へ変更してください。旧namespaceのwrapperや旧DLLは作成しません。Core単独のNuGet packageは公開せず、本体nupkgに同じbuildのCore DLL/XMLを同梱します。

入力準備/spool、画像資源、workbook/cell走査、style intern、幾何・ページ計画/構築、文字placement、セルcommand生成、診断集計、基本PDF描画/SaveはCoreへ集約しています。本体の公開型、完全font painter、画像変形、図形、リンク、PNG/SVG/Markdown、選択/trim、連続出力は本体に残ります。

Full reader/layout/drawing/PDF adapterが高機能部分を拡張し、本体の公開APIは既存assemblyに維持します。[構成](core-architecture.ja.md)、[検証結果](core-refactor-validation.md)と[所有者表](core-source-map.md)を参照してください。

同じload contextの同じCore DLLを利用する本体/Core converterは共有font gateを使います。外部PDFsharp利用者や別AssemblyLoadContextのCoreは同期対象外です。本体とCoreは同じbuildから配布してください。
