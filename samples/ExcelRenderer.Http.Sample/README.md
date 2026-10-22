# PDF delivery and file conversion sample

[日本語](README.ja.md) · [Main/Core choice and tests](../../docs/main-core-parity.md) · [Diagnostic policy](../../docs/diagnostics.md)

This nonpackable .NET 10 sample uses ProjectReference to this checkout. Installed applications
reference the published `ExcelRenderer` package, which bundles Core; do not look for a separate
Core NuGet package. ASP.NET dependencies belong only to this sample.

From the repository root (pass absolute paths because the Web SDK changes the child working directory):

```sh
dotnet run --project samples/ExcelRenderer.Http.Sample -- \
  "$PWD/tests/ExcelRenderer.Tests/SampleInputs/01-japanese.xlsx" \
  "$PWD/third_party/NotoSansJP/NotoSansJP-Regular.ttf"
curl http://localhost:5000/pdf -o report.pdf
curl http://localhost:5000/core-pdf -o core-report.pdf
curl http://localhost:5000/pdf-validated -o validated.pdf
curl http://localhost:5000/core-pdf-validated -o core-validated.pdf
```

Use `ASPNETCORE_URLS=http://127.0.0.1:5000` to fix the listening address explicitly.
Paths are server-owned command-line configuration; this is not an upload API.
Main uses RenderAsync + SingleStreamOutputSink; Core uses ConvertAsync.
[The actual handlers](PdfHttpEndpoints.cs) log code and location without cell values/messages,
forward RequestAborted and leave Response.Body open. Kestrel keeps synchronous I/O disabled.
There is no second File/JSON return after writing a PDF, and no Task.Run wrapper.

Direct endpoints log diagnostics after conversion. If the response has already begun, diagnostics
cannot retract it. Validated endpoints generate a unique temporary final PDF, reject the explicit
business codes FormulaCachedValueMissing/ImageDecodeFailed with 422 ProblemDetails (codes/counts
only), or copy the PDF asynchronously and delete the temporary after closing it. Every failure,
rejection and cancellation path cleans up. Extra disk I/O delays the first response byte.
This application file is distinct from the library's intermediate spool; neither route sends
pages immediately as they are laid out, and native/CPU processing remains synchronous.

Unselected warnings are logged and permitted; this example policy does not certify arbitrary
spreadsheets. Core has no StrictMode/Severity API. Main's Completed result is checked, and thrown
conversions cannot become successful files. See the diagnostic guide for retention limits.

Before response start, server-owned invalid/missing workbooks, missing/invalid configured fonts
and conversion/I/O failures are generic 500 responses. Rejections are 422. A client-input endpoint
would need separate input provenance/validation to choose 400/413; a missing configured font must
remain a server fault. No raw exception details are returned. Request cancellation propagates and
is logged separately. Once Response.HasStarted is true, errors propagate to host connection policy;
no JSON is appended to a partially delivered PDF. A client may need to discard a partial response.

For filesystem output (Ctrl+C cancels), these compiled examples create an adjacent temporary,
close it and replace the destination only on success:

```sh
# Main, using this existing sample project
dotnet run --project samples/ExcelRenderer.Http.Sample -- --file \
  "$PWD/tests/ExcelRenderer.Tests/SampleInputs/01-japanese.xlsx" /tmp/main.pdf \
  "$PWD/third_party/NotoSansJP/NotoSansJP-Regular.ttf"
# Core; optional fourth argument selects a sheet
dotnet run --project samples/ExcelRenderer.Core.Sample -- \
  "$PWD/tests/ExcelRenderer.Tests/SampleInputs/01-japanese.xlsx" /tmp/core.pdf \
  "$PWD/third_party/NotoSansJP/NotoSansJP-Regular.ttf"
```

[Main file implementation](PdfFileExample.cs) and [Core file implementation](../ExcelRenderer.Core.Sample/Program.cs)
print page counts/diagnostics. Libraries do not close caller streams. Main flushes its sink;
Core leaves flushing to the caller/host. The destination directory must already exist. Existing output survives conversion failure; atomicity
and power-loss durability depend on the destination filesystem.

The existing AsyncPdfStreamTests covers direct output on synchronous-I/O-disabled loopback
Kestrel. This change adds no HTTP endpoint tests. PdfFileExampleTests checks file publication
and preservation of previous output.
