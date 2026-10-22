# PDFのHTTP出力・ファイル変換サンプル

[English](README.md) · [本体/Core比較](../../docs/main-core-parity.ja.md) · [診断ポリシー](../../docs/diagnostics.ja.md)

.NET 10の非配布サンプルです。リポジトリ内ではProjectReferenceを使います。公開パッケージの
利用者はExcelRendererを参照してください。Core DLLは同梱で、単独Core NuGetはありません。
ASP.NET依存はサンプルだけです。

リポジトリrootで実行（Web SDKで作業ディレクトリが変わるため、パスは絶対パスで渡します）：

```sh
dotnet run --project samples/ExcelRenderer.Http.Sample -- \
  "$PWD/tests/ExcelRenderer.Tests/SampleInputs/01-japanese.xlsx" \
  "$PWD/third_party/NotoSansJP/NotoSansJP-Regular.ttf"
curl http://localhost:5000/pdf -o report.pdf
curl http://localhost:5000/core-pdf -o core-report.pdf
curl http://localhost:5000/pdf-validated -o validated.pdf
curl http://localhost:5000/core-pdf-validated -o core-validated.pdf
```

待受先を固定するには `ASPNETCORE_URLS=http://127.0.0.1:5000` を設定します。
入力とフォントはサーバー所有の起動引数で、アップロードAPIではありません。
本体はRenderAsync + SingleStreamOutputSink、CoreはConvertAsyncを使用。
[実ハンドラー](PdfHttpEndpoints.cs)はコードと位置をログへ記録し、セル値・Messageは記録しません。
RequestAbortedを伝達し、Response.Bodyを閉じません。Kestrelの同期I/O禁止を維持し、
PDF後のFile/JSON二重返却やTask.Runによる見せかけの非同期化はしません。

直接出力では変換後の診断取得時に送信済みの場合があり、撤回できません。検証後出力は一意な
最終PDF一時ファイルを作り、業務上の明示拒否コードFormulaCachedValueMissing/ImageDecodeFailedで
422 ProblemDetails（コードと件数のみ）を返すか、PDFを非同期コピー後にcloseして削除します。
成功・拒否・失敗・キャンセルで後始末します。追加ディスクI/Oと最初の応答までの遅延が発生します。
この最終PDFファイルとライブラリの内部中間スプールは別物です。どちらも配置したページから
即時送信する方式ではなく、CPU/native処理は同期です。

拒否対象外の診断はログとPDFが両立します。任意の表計算の完全性を検証するポリシーではありません。
CoreにStrictModeやSeverityはありません。本体はCompletedを確認し、例外になった変換を
成功ファイルにしません。診断保持の上限もガイドを参照してください。

送信開始前は、サーバー所有の不正/不存在ブック、設定フォント不正/不存在、変換/I/O失敗を
一般的な500に分類。業務診断拒否は422です。利用者入力を受けるAPIなら別途入力の出所・検証に
基づいて400/413を選びますが、設定フォントの不備はサーバー障害です。例外詳細は返しません。
キャンセルは別途ログし伝播させます。Response.HasStarted後はJSONを追加せずホストの例外・
接続終了方針へ委ねます。クライアントには途中PDFの破棄が必要な場合があります。

ファイル出力の実行例（Ctrl+Cでキャンセル）：

```sh
# 本体。既存HTTPサンプルのファイル出力モード
dotnet run --project samples/ExcelRenderer.Http.Sample -- --file \
  "$PWD/tests/ExcelRenderer.Tests/SampleInputs/01-japanese.xlsx" /tmp/main.pdf \
  "$PWD/third_party/NotoSansJP/NotoSansJP-Regular.ttf"
# Core。省略可能な第4引数はシート名
dotnet run --project samples/ExcelRenderer.Core.Sample -- \
  "$PWD/tests/ExcelRenderer.Tests/SampleInputs/01-japanese.xlsx" /tmp/core.pdf \
  "$PWD/third_party/NotoSansJP/NotoSansJP-Regular.ttf"
```

[本体実装](PdfFileExample.cs)と[Core実装](../ExcelRenderer.Core.Sample/Program.cs)はページ数・診断を
表示し、一意な隣接一時ファイルへ生成、close後に成功したものだけを置換します。
ライブラリは呼出側Streamを閉じません。本体sinkはflush、Coreは呼出側/ホストに任せます。
出力先ディレクトリは事前に用意してください。変換失敗で旧成果物を壊しませんが、ファイルシステム依存の原子性・電源断耐性は保証しません。

AsyncPdfStreamTestsで実4ルートを同期I/O禁止Kestrel上で確認します。
ValidatedPdfHttpTestsはキャンセル・書込失敗を確実に注入し、後始末、拒否、コードだけのログ、
サーバーエラー分類、並列リクエストの分離を確認。
PdfFileExampleTestsは成果物の公開と旧ファイル保持を検証します。
