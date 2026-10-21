# Direct PDF responses

This .NET 10 Kestrel sample keeps synchronous I/O disabled and writes final PDF bytes
directly to `Response.Body`. Main uses `RenderAsync` with `SingleStreamOutputSink`;
Core uses `ConvertAsync` with one supplied regular font. ASP.NET dependencies belong
to this nonpackable sample, not the libraries.

```sh
dotnet run --project samples/ExcelRenderer.Http.Sample -- \
  tests/ExcelRenderer.Tests/SampleInputs/01-japanese.xlsx \
  third_party/NotoSansJP/NotoSansJP-Regular.ttf
curl http://localhost:5000/pdf -o report.pdf
curl http://localhost:5000/core-pdf -o core-report.pdf
```

The workbook and font paths are server-owned command-line arguments. `ASPNETCORE_URLS`
can select a different listening address. Both endpoints set Content-Type and
Content-Disposition, forward RequestAborted, and leave Response.Body open without
returning a second File/JSON result. No Content-Length is required.

All pages are generated into the existing memory/file spool before final output.
CPU reading/layout/drawing remain synchronous. Final output is sequential asynchronous
I/O with bounded aggregation/transfer buffers; this does not send the first page
immediately or eliminate font/image/layout memory. Main flushes once before artifact
completion; Core leaves flushing to the HTTP host. If output fails after the response
starts, already sent bytes cannot be replaced with an error document; the host handles
the exception/connection. Library callers own their input/output streams.

`AsyncPdfStreamTests` runs these exact handlers against real loopback Kestrel with
`AllowSynchronousIO = false` and validates both responses with PdfPig.

日本語: 同期 I/O 禁止の Kestrel に、本体と Core の両 API から直接 PDF を出力するサンプルです。
中間スプールと同期 CPU 処理は残り、非同期化の対象は最終送信です。送信開始後のエラーは
送信済みデータを取り消せないため、ホスト側で処理してください。
