# ファイル変換サンプル

[English](README.md) · [本体/Core比較](../../docs/main-core-parity.ja.md) · [診断ガイド](../../docs/diagnostics.ja.md)

.NET 10の非配布コンソールサンプルです。リポジトリ内ではProjectReferenceを使います。
公開パッケージ利用者はExcelRendererを参照してください。Core DLLは同梱で、単独Core NuGetはありません。
ASP.NET依存はありません。

リポジトリrootで実行します。出力先ディレクトリは事前に用意してください。

```sh
# 本体
dotnet run --project samples/ExcelRenderer.Sample -- \
  "$PWD/tests/ExcelRenderer.Tests/SampleInputs/01-japanese.xlsx" /tmp/main.pdf \
  "$PWD/third_party/NotoSansJP/NotoSansJP-Regular.ttf"
# Core。省略可能な第4引数はシート名
dotnet run --project samples/ExcelRenderer.Core.Sample -- \
  "$PWD/tests/ExcelRenderer.Tests/SampleInputs/01-japanese.xlsx" /tmp/core.pdf \
  "$PWD/third_party/NotoSansJP/NotoSansJP-Regular.ttf"
```

[本体実装](PdfFileExample.cs)と[Core実装](../ExcelRenderer.Core.Sample/Program.cs)は、一意な隣接一時ファイルへ
生成し、closeとキャンセル確認後に、成功したものだけを目的ファイルへ置換します。
失敗・キャンセル時は一時ファイルを削除し、旧成果物を保持します。電源断耐性は保証しません。

両方ともページ数・診断を表示し、Ctrl+Cでキャンセルします（終了コード2）。
ライブラリは呼出側Streamを閉じません。サンプルは自分で開いたStreamを閉じます。
本体はRenderAsync + SingleStreamOutputSinkでCompletedを確認、CoreはConvertAsyncと通常体1個を使用します。
最終PDF全体のToArray/MemoryStream複製は行いません。
PdfFileExampleTestsでファイル公開と失敗・キャンセル時の旧成果物保持を確認します。
