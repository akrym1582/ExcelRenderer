# ExcelRenderer.Tool

Convert Excel (`.xlsx`) workbooks to PDF, PostScript, PNG or SVG from the command line. Populate Excel templates with JSON data to generate XLSX reports or render them directly to documents and images.

## Install

Install the .NET 10 SDK, then run:

```bash
dotnet tool install --global ExcelRenderer.Tool
excelrenderer --help
```

Update an existing installation with:

```bash
dotnet tool update --global ExcelRenderer.Tool
```

The tool includes rendering, template mapping, bundled Japanese and emoji fonts, and SkiaSharp Linux native assets. No separate ExcelRenderer NuGet packages are needed. On Debian/Ubuntu, install `libfontconfig1` if it is not already available.

## Convert a workbook

```bash
excelrenderer pdf input.xlsx -o output.pdf
excelrenderer ps input.xlsx -o output.ps
excelrenderer image input.xlsx -o ./png-output
excelrenderer svg input.xlsx -o ./svg-output
```

PDF and PostScript produce one document. PNG and SVG produce one image per worksheet page. Workbook print settings control page size, orientation, margins and scaling.

Select worksheets or supply a font file:

```bash
excelrenderer pdf input.xlsx -o output.pdf --sheet Sheet1 Sheet3
excelrenderer pdf input.xlsx -o output.pdf --font-file ./NotoSansJP-Regular.ttf
```

## Generate a report from JSON

Enter `**Customer` in cell B2 of a workbook and save it as `template.xlsx`. Create `data.json`:

```json
{ "Customer": "Alice" }
```

Generate a workbook, or map and render it in one command:

```bash
excelrenderer xlsx template.xlsx --data data.json -o report.xlsx
excelrenderer pdf template.xlsx --data data.json -o report.pdf
```

The generated workbook contains `Alice` in B2. Property names are case-sensitive; keep the input and output paths different.

## Command reference

| Command | Output |
| --- | --- |
| `xlsx` | XLSX populated from a template and JSON data |
| `pdf` | PDF document |
| `ps` | PostScript document |
| `image` | PNG images, one per page |
| `svg` | SVG images, one per page |
| `render` | Selectable format with additional layout and diagnostic options |
| `core` | PDF using the Core renderer and saved cell values |

Use command help for available options:

```bash
excelrenderer pdf --help
excelrenderer render --help
excelrenderer xlsx --help
```

Saved formula results are used by default. Rendering may differ from Microsoft Excel; see the guides for supported features and limitations.

## Guides and examples

- [Full CLI reference and examples](https://github.com/akrym1582/ExcelRenderer/blob/main/README.md#command-line-usage)
- [Template mapping guide](https://github.com/akrym1582/ExcelRenderer/blob/main/docs/mapping.md)
- [Runnable invoice sample](https://github.com/akrym1582/ExcelRenderer/blob/main/samples/mapping/invoice/README.md)
- [Diagnostics and output delivery](https://github.com/akrym1582/ExcelRenderer/blob/main/docs/diagnostics.md)
- [Compatibility and rendering notes](https://github.com/akrym1582/ExcelRenderer/blob/main/README.md#compatibility-and-rendering-notes)
- [日本語の使い方](https://github.com/akrym1582/ExcelRenderer/blob/main/README.ja.md#cli-の使い方)

## License

ExcelRenderer is MIT licensed. Bundled fonts have separate licenses; see [third-party notices](https://github.com/akrym1582/ExcelRenderer/blob/main/THIRD-PARTY-NOTICES.md).
