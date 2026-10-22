# File conversion samples

[日本語](README.ja.md) · [Main/Core choice](../../docs/main-core-parity.md) · [Diagnostics](../../docs/diagnostics.md)

These nonpackable .NET 10 console examples use ProjectReference to this checkout.
Installed applications reference the published ExcelRenderer package, which bundles Core.
There is no standalone Core NuGet package. The examples have no ASP.NET dependency.

From the repository root, with an existing output directory:

```sh
# Main
dotnet run --project samples/ExcelRenderer.Sample -- \
  "$PWD/tests/ExcelRenderer.Tests/SampleInputs/01-japanese.xlsx" /tmp/main.pdf \
  "$PWD/third_party/NotoSansJP/NotoSansJP-Regular.ttf"
# Core; an optional fourth argument selects a sheet
dotnet run --project samples/ExcelRenderer.Core.Sample -- \
  "$PWD/tests/ExcelRenderer.Tests/SampleInputs/01-japanese.xlsx" /tmp/core.pdf \
  "$PWD/third_party/NotoSansJP/NotoSansJP-Regular.ttf"
```

[Main implementation](PdfFileExample.cs) and [Core implementation](../ExcelRenderer.Core.Sample/Program.cs)
write to a unique temporary file beside the destination, close it, check cancellation, and
replace the destination only on success. Failure/cancellation removes the temporary and
preserves the previous output. Filesystem/power-loss durability is not guaranteed.

Both print page counts and diagnostics. Ctrl+C cancels conversion (exit code 2).
The libraries leave caller-owned streams open; the examples close the streams they open.
Main uses RenderAsync with SingleStreamOutputSink and checks Completed; Core uses ConvertAsync
with one supplied regular font. No whole final-PDF ToArray/MemoryStream copy is made.
PdfFileExampleTests verifies publication and preservation of previous output on failure/cancel.
