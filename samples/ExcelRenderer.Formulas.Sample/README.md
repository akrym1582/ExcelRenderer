# Formula invoice sample

This repository sample uses source ProjectReferences. For an isolated example
using published `ExcelRenderer.Mapping`/`ExcelRenderer` 1.9.1 packages, see the
[package API sample](../namespace-migration/README.md). Formulas/OpenXml are bundled
assemblies, not separate NuGet packages; see the [namespace migration](../../docs/namespace-migration.md).

Run from the repository root with .NET 10:

```sh
dotnet run --project samples/ExcelRenderer.Formulas.Sample -c Release -- /tmp/formula-invoice-3 3
```

The optional arguments are a new/empty output directory, line count (0–1000, default 3), and a regular TrueType font path. The default font is the repository's `third_party/NotoSansJP/NotoSansJP-Regular.ttf`. Native rendering dependencies are the same as the main library.

The executable creates `template.xlsx`, replaces quantity/code placeholders, expands detail rows, rewrites formulas and the `Amounts` defined range, recalculates, saves `invoice.xlsx`, and verifies its saved total. The formula chain uses exact VLOOKUP from a Master sheet for name/price, quantity × price, a fixed tax rate on `Rates!$A$1`, ROUND, SUM, category SUMIFS/COUNTIFS, DATE/EOMONTH and TEXT year/month. `invoice-input.xlsx` retains only the Mapping expansion and reference rewriting with all caches invalidated; both Excel and the engine can calculate that identical file. Copied formulas are asserted against fixed expected formulas before calculation. The initial deliberately stale formula cache is 999. It then renders the Invoice sheet to PDF, PNG, SVG and Markdown using explicit recalculation. No external spreadsheet engine is used.

Use separate directories to exercise zero, one and multiple-page lines:

```sh
dotnet run --project samples/ExcelRenderer.Formulas.Sample -c Release -- /tmp/formula-invoice-0 0
dotnet run --project samples/ExcelRenderer.Formulas.Sample -c Release -- /tmp/formula-invoice-1 1
dotnet run --project samples/ExcelRenderer.Formulas.Sample -c Release -- /tmp/formula-invoice-70 70
```

Expected totals are 0, 11, 66 and 27335 for 0, 1, 3 and 70 lines. The named total range includes the surviving heading row, so deleting every detail row still gives SUM a valid empty numeric population. See the [syntax/function reference](../../docs/formulas-reference.md) for supported features and limits, and the [formula guide](../../docs/formulas.md) for configuration; this is not an Excel interoperability certification.
