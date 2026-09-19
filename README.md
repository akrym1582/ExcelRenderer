# ExcelRenderer

ExcelRenderer is a .NET library for rendering Excel (`.xlsx`) worksheets as PDF documents or page-by-page PNG images.

It separates workbook parsing, layout, drawing-command generation, and output rendering into distinct stages:

```text
Excel (.xlsx)
    -> ReportDocument
    -> RenderDocument
    -> DrawCommand
    -> PDF / PNG
```

This makes the rendering pipeline easier to test, understand, and extend with new layout behavior or output formats.

> [!NOTE]
> ExcelRenderer is currently an MVP and does not aim for pixel-perfect parity with Microsoft Excel.

## Features

- Reads `.xlsx` workbooks with ClosedXML
- Supports cell text, fonts, alignment, wrapping, fills, and borders
- Handles merged cells, hidden rows and columns, column widths, and row heights
- Honors print areas, page size, orientation, margins, and scaling settings
- Splits output into pages at row and column boundaries
- Renders worksheet PNG and JPEG images
- Renders header and footer text
- Produces PDF output with PDFsharp
- Produces one PNG image per page with SkiaSharp
- Exports AI-friendly Markdown with merged-cell HTML, layout-aware reading order, formulas, and external images

## Requirements

- .NET 10 SDK to build and test the repository
- A target framework compatible with .NET Standard 2.1 to consume the library
- Appropriate fonts installed or supplied through `PdfSharpFontResolver`

## Quick start

### High-level library API

After building a local `ExcelRenderer` NuGet package (or referencing the project), the facade API performs the complete read, layout, render, and write pipeline:

```csharp
using ExcelRenderer;

await ExcelConverter.ConvertToPdfAsync("input.xlsx", "output.pdf");
await ExcelConverter.ConvertToImagesAsync("input.xlsx", "./images");
await ExcelConverter.ConvertToMarkdownAsync("input.xlsx", "output.md");
```

Use `PdfExportOptions`, `ImageExportOptions`, and `MarkdownExportOptions` to select a worksheet or configure format-specific behavior. Existing output files, and non-empty image output directories, are not overwritten.

### Command-line tool

The CLI package is not yet published to nuget.org. To try it, first create and install the local package:

```bash
dotnet pack src/ExcelRenderer.Tool/ExcelRenderer.Tool.csproj -c Release -o artifacts
dotnet tool install --global --add-source ./artifacts ExcelRenderer.Tool
```

Then convert workbooks with the `pdf`, `image`, or `markdown` (`md`) commands:

```bash
excelrenderer pdf input.xlsx -o output.pdf
excelrenderer image input.xlsx -o ./images
excelrenderer md input.xlsx -o output.md
```

Run `excelrenderer --help` or a subcommand's `--help` for options such as `--sheet`, `--dpi`, and Markdown image/layout controls.

### Low-level rendering API

Reference the `ExcelRenderer` project, then run the workbook through the layout and rendering pipeline:

```csharp
using ExcelRenderer.Drawing;
using ExcelRenderer.Excel;
using ExcelRenderer.Layout;
using ExcelRenderer.PdfSharp;

var document = new ExcelReader().Read("report.xlsx");
var sheet = document.Sheets[0];

var layoutEngine = new ReportLayoutEngine(new PdfSharpTextMeasurer());
var renderDocument = layoutEngine.Layout(sheet);
var commands = new DrawCommandGeneratorPass().Generate(renderDocument);

using var output = File.Create("report.pdf");
new PdfSharpRenderer().Render(commands, sheet.PageSettings, output);
```

To render the same commands as page-by-page PNG files:

```csharp
using ExcelRenderer.SkiaSharp;

new PngRenderer().Render(
    commands,
    sheet.PageSettings,
    pageNumber => File.Create($"report-{pageNumber}.png"),
    dpi: 144);
```

To convert an entire workbook to Markdown and extract its images:

```csharp
using ExcelRenderer.Markdown;

await ExcelMarkdownConverter.ConvertAsync("sample.xlsx", "output");
```

This creates `output/sample.md` and image files under `output/images`. Use
`MarkdownExportOptions` to control formula/address metadata, hidden rows and columns,
nearby image text, layout analysis, and image export. Markdown export consumes the
same `ReportDocument` model as the renderers and does not alter the PDF/PNG pipeline.

For environments where the workbook font is not installed, configure a font resolver before PDFsharp first accesses a font:

```csharp
using ExcelRenderer.PdfSharp;
using PdfSharp.Fonts;

GlobalFontSettings.FontResolver = new PdfSharpFontResolver(
    "Noto Sans JP",
    "/app/fonts/NotoSansJP-Regular.ttf");
```

## Architecture

ExcelRenderer uses four main stages:

1. `ExcelReader` converts a workbook into the library's report model.
2. `ReportLayoutEngine` runs focused layout passes and creates a paginated `RenderDocument`.
3. `DrawCommandGeneratorPass` converts laid-out cells and images into renderer-independent commands.
4. `PdfSharpRenderer` or `PngRenderer` writes the final output.

The [Japanese guide](README.ja.md) contains a detailed description of the models, layout passes, extension points, and rendering pipeline.

## Current limitations

- Only the first print area is used when a worksheet defines multiple print areas.
- Images are positioned from their top-left Excel anchor and are not split across pages.
- Charts are not supported.
- Formula behavior and conditional formatting are not reproduced completely.
- Not every paper size or header/footer formatting code is supported.
- Page breaks occur only at row and column boundaries.
- Output can differ from Excel because font measurement and rendering engines differ.

## Development

Run the test suite from the repository root:

```bash
dotnet test ExcelRenderer.slnx
```

Production code is under `src/ExcelRenderer`, and tests are under `tests/ExcelRenderer.Tests`.

## Documentation

- [Detailed guide (Japanese)](README.ja.md)
- [Contributing instructions for GitHub Copilot](.github/copilot-instructions.md)

## License

ExcelRenderer is available under the [MIT License](LICENSE).
