# ExcelRenderer Core

> 明示再計算モードを追加しました。[数式計算ガイド](formulas.ja.md)が評価範囲・キャッシュ失効・対応機能の現在の仕様です。以下の保存値／数式拒否の説明は、特記のない限り既定の CachedValue モードに適用します。

[English](core.md) | 日本語

XLSXからPDFだけを生成する独立したソースプロジェクトです。`netstandard2.1`で、.NET 8/10からProjectReferenceできます。Core単独のNuGetパッケージ、CLI、フォント入りDLLは作成しません。本体パッケージには同じbuildのCore DLL/XMLを同梱します。

公開型には `ExcelConverter`、`PdfExportOptions`、`WorkbookInputOptions`、`ConversionResult`、`ConversionDiagnostic` があります。名前空間は `ExcelRenderer.Core` で、設定や結果のメンバーは以下に示すCore版の対応範囲に合わせています。

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

呼出し側が指定する単体の固定 TrueType sfnt、または TTC/OTC 内の選択 face を使います。FontCollectionIndex（0始まり、既定0）で選び、計測・描画前に同じ単体 face へ正規化します。既存の非可変カラー face は共通 Skia カラー経路を使います。拡張子では判定しません。CFF/CFF2・可変・WOFF/WOFF2・旧 true/typ1 は PDF 対象外です。破損・未対応形式は InvalidDataException、不存在は FileNotFoundException、不正 index は引数エラーです。[形式の契約](managed-truetype-subset.ja.md)を参照してください。

フォントbytesは変換ごとに一度読み、同じsnapshotで列幅・行高・文字幅・PDF描画を処理します。セルとヘッダー/フッターは通常書体に統一します。太字・斜体の疑似描画、フォント探索・fallback、IVSの異体字形の再現、外字・カラー絵文字の特別処理はありません。セルとヘッダー/フッターの異体字セレクター（U+FE00–U+FE0F、U+E0100–U+E01EF）は計測・描画前に除去し、基底文字を表示します。例えば「葛」＋IVSは「葛」として表示し、セレクター由来の追加の豆腐や文字幅は生成しません。基底文字自体が指定フォントに未収録の場合の表示は保証しません。サイズ・色・下線を維持します。未収録文字は指定フォント/Skiaの欠字表示に従い、すべてのUnicode文字の表示は保証しません。フォント置換によりExcel原本と列幅や折返しが変わることがあります。

## 印刷と画像

nullのSheetNameは非表示状態でも追加フィルターせず、ブック順に全シートを出力します。単一SheetNameを指定でき、不存在は`ArgumentException`です。空シートは1ページです。Excel内の単一/複数印刷範囲、用紙、余白、倍率、FitToPages、ページ順、繰返し行列、改ページ、結合セル、背景、罫線、配置、折返し、ShrinkToFit、ヘッダー/フッターを維持します。ヘッダー日時は1変換に1つのsnapshotです。セル文字の回転は無視します。

画像は元画像全体を配置枠へ伸縮し、位置・サイズ・Z順を維持します。開始点が所属する印刷領域のページにだけ出力し、はみ出しはPDFページ境界で切れます。開始点が印刷範囲外なら省略します。後続ページへの複製・分割、繰返し行列由来の画像複製、crop・回転・反転を行いません。同じ開始点を含む複数印刷範囲では領域ごとに印刷します。

図形・吹き出し・図形内文字・クリック可能なPDFリンクは生成しません。リンク付きセルの表示文字を保持し、HYPERLINKを含む数式は元XMLの型付き保存結果だけを表示します。OpenXmlが不変の保存値を直接書式化します。保存結果がなければ空表示と診断を返し、friendly textでは補いません。数式モードの既定はCachedValueで、Legacy は入力処理前に拒否し、Recalculate は数式ガイドに記載した限定計算器を使います。通常セルは通常の書式処理に従います。仕様上無視する図形や画像加工ごとの大量警告は作りません。画像decode失敗などの非致命的な診断はresult.Diagnosticsで取得できます。

APIのセル範囲、ページ抽出、trim、連続画像、PNG/SVG/Markdownはありません。

## Stream・資源契約

入力は現在位置から読み、seekable/nonseekableに対応します。出力は書込み可能・seekable・開始位置0・長さ0が必須です。呼出し元の両streamを閉じません。非seekable出力向けの全PDFbufferはありません。キャンセルはOperationCanceledExceptionです。読込み・ページ境界・Saveの書込み境界で確認します。同期ライブラリ処理を瞬時には中断できません。

入力既定値はメモリ16MiB、最大128MiB、ZIP entry 10,000、展開合計512MiB、一時ファイル不許可です。許可したspoolのtempは成功・失敗で削除します。Save時のI/O失敗やキャンセルで途中データが残る場合があり、API全体の原子的書込みは保証しません。[sample](../samples/ExcelRenderer.Core.Sample/Program.cs)は同じディレクトリの一時ファイルへ書き、streamを閉じてから目的ファイルへ移動し、失敗時は一時ファイルを削除します。

```bash
dotnet run --project samples/ExcelRenderer.Core.Sample -c Release -- input.xlsx output.pdf /fonts/font.ttf
```

font収集と描画の各passで1ページのpayloadだけを生成します。Coreが共通Skia文書・subset・
基本描画・incremental finalizerを所有します。文字cacheは512件・2048文字まで、decoded画像の
推定cache上限は64MiBです。PDF中間spoolは8MiBを超えると一時ファイルを使い、成功・失敗時に
削除します。入力spoolの設定とは独立しています。Skia文書の資源も保持するため定メモリではありません。

フォントは一度の読込によるimmutable snapshotで計測・描画し、ptのadvanceと96dpiの丸めMDWを
区別します。グローバルresolver/reset/gateはありません。main/Coreの混在並列でもnative資源を
変換scopeごとに所有します。Core にも SkiaSharp native assets が必要です。通常 TrueType の subset・TTC 抽出は C# で実装しています。
Pythonや外部PDF readerは製品runtimeの要件ではありません。

限定finalizerは用紙box精度0.001pt、RGB page group、画像補間を維持し、元のcontent/font/画像
stream bytesを保持します。画像辞書の追記revisionは圧縮済み画像bytesを再掲するため、画像の
多い出力は大きくなる場合があります。

## ソースと保守

取り込み元はmain 1.7.2、コミット`32e9165d145516dbd0bcb4c2ae31a78ce6774bce`です。[ファイル対応表](core-source-map.md)に選択ソースの移植元・移植先を記録しています。既存本体・Fonts・Toolへの参照、既存ソースのCompile Include、wrapperはありません。共通処理の修正はCoreへ行い、本体とCoreの方針差を両方の回帰テストで確認します。依存versionは基準版と同じです。テストにコピーするNoto素材は既存OFLと著作権表示を維持し、Core本体には埋め込みません。

## 検証

[検証結果と再現手順](core-validation.md)を参照してください。Windows CIの結果は、このLinux環境の実行結果とは区別します。

ファイルの対応・所有者・製品ごとの差分は[所有者表](core-source-map.md)を参照してください。

ヘッダー/フッターは既存のフィールド置換・配置を継承します。Excel独自の書式制御コードを新たに解析する処理は追加していません。LinuxのSkia native層は初期化時にfontconfigへアクセスする場合があります。指定フォント以外のfaceは選択しませんが、native層の初期化アクセスが皆無とは主張しません。

## 共通実装

入力準備/spool、画像資源、workbook/cell走査、style intern、幾何・ページ計画/構築、文字placement、セルcommand生成、診断集計、基本PDF描画/SaveはCoreへ集約しています。本体の公開型、完全font painter、画像変形、図形、リンク、PNG/SVG/Markdown、選択/trim、連続出力は本体に残ります。

Full reader/layout/drawing/PDF adapterが高機能部分を拡張し、本体の公開APIは既存assemblyに維持します。[構成](core-architecture.ja.md)、[検証結果](core-refactor-validation.md)と[所有者表](core-source-map.md)を参照してください。

本体/Coreは同じ共通実装を利用し、資源は変換単位で所有します。本体とCoreは同じbuildから配布してください。[撤去の検証記録](pdfsharp-removal-validation.md)も参照してください。

Core の Reader は `PdfExportOptions.ReadOptions` から選択できます。[エンジン選択](openxml-integration.ja.md)を参照してください。汎用の入力検査・spool は共有 OpenXml 依存に移管しています。

コレクションは `PdfExportOptions.FontCollectionIndex`（0始まり、既定0）で選択します。[形式の詳細](managed-truetype-subset.ja.md)を参照してください。
