# ExcelRenderer

> PDF output keeps ordinary text searchable, but supported ideographic variation sequences (IVS) are emitted as vector outlines so the selected format 14 glyph is preserved. Those outlined IVS characters are not searchable or copyable as text.

ExcelRenderer is a .NET library for rendering Excel (`.xlsx`) worksheets as PDF documents or page-by-page PNG and SVG images.

It separates workbook parsing, layout, drawing-command generation, and output rendering into distinct stages:

```text
Excel (.xlsx)
    -> ReportDocument
    -> RenderDocument
    -> DrawCommand
    -> PDF / PNG / SVG
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
- Produces one self-contained SVG per page with outlined text and embedded images
- Exports AI-friendly Markdown with merged-cell HTML, layout-aware reading order, formulas, and external images

## Requirements

- .NET 10 SDK to build and test the repository
- A target framework compatible with .NET Standard 2.1 to consume the library
- Appropriate fonts installed or supplied through `PdfSharpFontResolver`

The optional `ExcelRenderer.Fonts` package contains Noto Sans JP Regular TTF for
ordinary text, Noto Sans CJK JP for Gothic IVS rendering, IPAmj Mincho for
Mincho IVS rendering and fallback, and Noto Color Emoji. The `ExcelRenderer` library
does not depend on that package: install it alongside the library only when you
want those bundled resources and IVS fallback. Without it, the library uses
explicitly registered or system fonts. The CLI depends on this package and
installs it transitively. IVS rendering defaults to Gothic
(Noto Sans CJK JP, then IPAmj Mincho). Set `FontOptions.IvsFontStyle` to
`IvsFontStyle.Mincho`, or pass `--ivs-font-style mincho` to the CLI, to use
IPAmj Mincho directly. See [third-party notices](THIRD-PARTY-NOTICES.md).
Set `FontOptions.ReplaceIvsWithBaseCharacter` to `true` when the library should
render an ideographic variation sequence as its base character without the
variation selector instead of selecting its IVS glyph.


## Installation

The library, optional fonts, and command-line tool are separate NuGet packages. After the corresponding packages are published to NuGet.org, install them using the commands below. The tool installs its font dependency automatically.

### Library (NuGet)

Run this in your application's project directory (.NET Standard 2.1-compatible target required):

```bash
dotnet add package ExcelRenderer
dotnet add package ExcelRenderer.Fonts  # optional Japanese fonts
```

`ExcelRenderer` is sufficient when your application provides its own fonts.
`ExcelRenderer.Fonts` adds only the bundled font resources; it is not required
to use the library API. The package includes Noto Sans JP Regular, Noto Sans CJK JP, IPAmj Mincho,
and Noto Color Emoji under their respective licenses. See
[third-party notices](THIRD-PARTY-NOTICES.md) for redistribution requirements.

### Command-line tool (dotnet tool)

Install the .NET 10 SDK, then install the CLI from NuGet.org:

```bash
dotnet tool install --global ExcelRenderer.Tool
excelrenderer --help
```

The package name is `ExcelRenderer.Tool`; the executable command is `excelrenderer`.
The tool declares `ExcelRenderer` and `ExcelRenderer.Fonts` as dependencies, so
you do not need to install either package separately to use the CLI. These
commands assume NuGet.org is enabled in your NuGet sources.

To update an existing global installation:

```bash
dotnet tool update --global ExcelRenderer.Tool
```

For a project-local installation, run the following in the repository where you want to use the tool. Create the manifest only if one does not already exist:

```bash
dotnet new tool-manifest
dotnet tool install --local ExcelRenderer.Tool
dotnet tool run excelrenderer --help
```

Commit `.config/dotnet-tools.json` so other contributors can install the same tool version with `dotnet tool restore`.

## Quick start

### High-level library API

After installing `ExcelRenderer`, the facade API performs the complete read, layout, render, and write pipeline:

```csharp
using ExcelRenderer;

await ExcelConverter.ConvertToPdfAsync("input.xlsx", "output.pdf");
await ExcelConverter.ConvertToImagesAsync("input.xlsx", "./images");
await ExcelConverter.ConvertToSvgAsync("input.xlsx", "./svg-output");
await ExcelConverter.ConvertToMarkdownAsync("input.xlsx", "output.md");
```

For stream-based integrations, `RenderAsync` reads from the input's current position and leaves both
the input stream and a `SingleStreamOutputSink` output stream open. Use `DirectoryOutputSink` for
page-by-page PNG/SVG output. `RenderRequest` selects sheets by exact name (in request order) and
can select rendered PDF/PNG/SVG document pages; Markdown does not support page selection.
`ConversionManifest.WriteAsync` writes the completed artifact metadata and diagnostics as schema
version 1 JSON with relative artifact names.

Use `PdfExportOptions`, `ImageExportOptions`, `SvgExportOptions`, and `MarkdownExportOptions` to select a worksheet or configure format-specific behavior. Existing output files, and non-empty image or SVG output directories, are not overwritten.

### Command-line tool

After installing `ExcelRenderer.Tool`, convert workbooks with the `pdf`, `image`, `svg`, or `markdown` (`md`) commands:

```bash
excelrenderer pdf input.xlsx -o output.pdf
excelrenderer image input.xlsx -o ./images
excelrenderer svg input.xlsx -o ./svg-output
excelrenderer md input.xlsx -o output.md
```

Run `excelrenderer --help` or a subcommand's `--help` for options such as `--sheet`, `--dpi`, and Markdown image/layout controls.

The unified `render` command can generate a single continuous image per selected worksheet without print-page margins, breaks, titles, or headers:

```bash
excelrenderer render input.xlsx -o ./continuous-images --format svg --image-layout continuous
```

Continuous layout is available only for PNG and SVG, cannot be combined with `--pages`, and uses the sheet's used range rather than its print area. PNG continuous output defaults to a 100 million-pixel limit (about 381 MiB for an RGBA bitmap before encoder overhead); set `RenderRequest.MaxPngPixels` only to a safe finite value when larger canvases are required.

### Low-level rendering API

Install the `ExcelRenderer` package or reference the project, then run the workbook through the layout and rendering pipeline:

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

To render one self-contained SVG file per page, with dimensions and coordinates in PDF points:

```csharp
new SvgRenderer().Render(
    commands,
    sheet.PageSettings,
    pageNumber => File.Create($"report-{pageNumber}.svg"));
```

SVG text is converted to vector paths so the viewer does not need the source font. This intentionally prevents text search and copy; it does not prevent editing. Raster images are embedded in each SVG. Text shaping has the same limitations as PNG rendering, and complex scripts or color/bitmap-only glyphs are not guaranteed.

To convert an entire workbook to Markdown and extract its images:

```csharp
using ExcelRenderer.Markdown;

await ExcelMarkdownConverter.ConvertAsync("sample.xlsx", "output");
```

This creates `output/sample.md` and image files under `output/images`. Use
`MarkdownExportOptions` to control formula/address metadata, hidden rows and columns,
nearby image text, layout analysis, and image export. Markdown export consumes the
same `ReportDocument` model as the renderers and does not alter the PDF/PNG pipeline.

PDF and PNG conversion use Noto Sans JP Regular TTF when `ExcelRenderer.Fonts` is installed. Simple emoji (one scalar, optionally followed by VS16) use Noto Color Emoji; PDF and SVG embed them as color bitmap images. Complex emoji sequences need a separate shaping implementation. Otherwise, the library resolves registered or OS fonts. To use an external font for PDFsharp, configure a resolver before PDFsharp first accesses a font:

```csharp
using ExcelRenderer.PdfSharp;
using PdfSharp.Fonts;

GlobalFontSettings.FontResolver = new PdfSharpFontResolver(
    "Noto Sans JP",
    "/app/fonts/NotoSansJP-Regular.ttf");
```

The unified `render` command also accepts `--font-policy bundled|requested`, repeated `--font-dir`,
`--font-file`, and `--fallback-font` options, and `--no-system-fonts`. `--font-file` reads each
font directly and registers its internal family and style; supported TrueType/OpenType content can
therefore be supplied as `.ttf`, Windows EUDC `.tte`, or `.otf` without relying on its extension.
For example: `--font-file /app/fonts/report.ttf --font-file C:\Windows\Fonts\EUDC.TTE`.
For BMP private-use characters (U+E000–U+F8FF), the requested font is tried first, followed by
these files in command-line order and then normal family fallbacks. If none contains the glyph,
`MissingPrivateUseGlyph` is reported and U+FFFD is rendered; `--strict` and
`--warnings-as-errors MissingPrivateUseGlyph` can make this a conversion failure. Font files must
be readable and valid, and users are responsible for their licenses and PDF embedding rights.
The option has no effect on Markdown output because Markdown does not render glyphs.
`bundled` is the default for compatibility;
`requested` prioritizes registered and supplied font directories before configured fallbacks and the
bundled font. Font fallback is selected per Unicode text element, so surrogate pairs and combining
sequences are not split.

## Architecture

ExcelRenderer uses four main stages:

1. `ExcelReader` converts a workbook into the library's report model.
2. `ReportLayoutEngine` runs focused layout passes and creates a paginated `RenderDocument`.
3. `DrawCommandGeneratorPass` converts laid-out cells and images into renderer-independent commands.
4. `PdfSharpRenderer`, `PngRenderer`, or `SvgRenderer` writes the final output.

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

To try the CLI from source before publishing, create and install a local package:

```bash
dotnet pack src/ExcelRenderer.Tool/ExcelRenderer.Tool.csproj -c Release -o artifacts/packages
dotnet pack src/ExcelRenderer/ExcelRenderer.csproj -c Release -o artifacts/packages
dotnet pack src/ExcelRenderer.Fonts/ExcelRenderer.Fonts.csproj -c Release -o artifacts/packages
dotnet tool install --tool-path ./artifacts/tool-test --add-source ./artifacts/packages ExcelRenderer.Tool
./artifacts/tool-test/excelrenderer --help
```

Run the test suite from the repository root:

```bash
dotnet test ExcelRenderer.slnx
```

Production code is under `src/ExcelRenderer`, and tests are under `tests/ExcelRenderer.Tests`.

## Documentation

- [Detailed guide (Japanese)](README.ja.md)
- [AI coding agent guide](AGENTS.md)

## License

ExcelRenderer is available under the [MIT License](LICENSE).

The optional `ExcelRenderer.Fonts` package contains the Japanese fonts and their
separate license texts; see [Third-party notices](THIRD-PARTY-NOTICES.md).
