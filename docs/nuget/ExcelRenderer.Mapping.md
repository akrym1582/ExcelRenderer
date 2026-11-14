# ExcelRenderer.Mapping

Generate Excel (`.xlsx`) reports from templates using C# objects or JSON. Design the layout in Excel, add placeholders, and supply the data from your application.

The mapping library targets .NET Standard 2.1 and works without Microsoft Excel, rendering libraries or fonts. It supports simple values, repeating rows, nested array blocks, formatting, image binding and manual page breaks.

## Install

```bash
dotnet add package ExcelRenderer.Mapping
```

Use a .NET Standard 2.1-compatible application, such as .NET 8 or .NET 10. .NET Framework is not supported.

## Map a C# object

Enter `**Customer` in cell B2 of a workbook and save it as `template.xlsx`. Then run:

```csharp
using ExcelRenderer.Mapping;

ExcelTemplateMapper.Map(
    "template.xlsx",
    "report.xlsx",
    new { Customer = "Alice" });
```

Cell B2 in `report.xlsx` contains `Alice`. The output is an ordinary XLSX workbook that can be opened in Excel or passed to a renderer.

Property names are case-sensitive. Keep the input and output paths different.

## Map JSON data

Create `data.json`:

```json
{ "Customer": "Alice" }
```

Read the JSON and map the same template:

```csharp
using System.IO;
using System.Text.Json;
using ExcelRenderer.Mapping;

using var document = JsonDocument.Parse(File.ReadAllText("data.json"));
ExcelTemplateMapper.Map(
    "template.xlsx",
    "report.xlsx",
    document.RootElement);
```

Keep the `JsonDocument` alive until mapping completes.

## Template reference

| Task | Guide |
| --- | --- |
| Build a complete invoice | [First workbook](https://github.com/akrym1582/ExcelRenderer/blob/main/docs/mapping.md#first-workbook-an-invoice) |
| Look up template directives and formatting | [Specification reference](https://github.com/akrym1582/ExcelRenderer/blob/main/docs/mapping.md#specification-reference) |
| Repeat rows and nested groups | [Array blocks](https://github.com/akrym1582/ExcelRenderer/blob/main/docs/mapping.md#multiple-row-and-nested-arrays) |
| Insert page breaks | [Page breaks](https://github.com/akrym1582/ExcelRenderer/blob/main/docs/mapping.md#page-breaks) |
| Bind images and font colors | [Comments, font colors and images](https://github.com/akrym1582/ExcelRenderer/blob/main/docs/mapping.md#template-comments-font-colors-and-bound-images) |
| Recalculate supported formulas explicitly | [Formula guide](https://github.com/akrym1582/ExcelRenderer/blob/main/docs/formulas.md) |

Formula calculation is opt-in and supports a limited subset of Excel formulas. See the mapping guide for default formula handling and calculation options.

## Render or use the command line

To convert the generated XLSX to PDF, PostScript, PNG or SVG from C#, add the [ExcelRenderer](https://www.nuget.org/packages/ExcelRenderer) package. See the [C# quick start](https://github.com/akrym1582/ExcelRenderer/blob/main/README.md#quick-start).

For command-line mapping and conversion, install [ExcelRenderer.Tool](https://www.nuget.org/packages/ExcelRenderer.Tool). The tool includes Mapping; no separate library installation is needed:

```bash
dotnet tool install --global ExcelRenderer.Tool
excelrenderer xlsx template.xlsx --data data.json -o report.xlsx
excelrenderer pdf template.xlsx --data data.json -o report.pdf
```

## More information

- [Complete mapping guide and API examples](https://github.com/akrym1582/ExcelRenderer/blob/main/docs/mapping.md)
- [Runnable invoice sample](https://github.com/akrym1582/ExcelRenderer/blob/main/samples/mapping/invoice/README.md)
- [日本語のマッピングガイド](https://github.com/akrym1582/ExcelRenderer/blob/main/docs/mapping.ja.md)
- [Source code and issues](https://github.com/akrym1582/ExcelRenderer)

## License

[MIT](https://github.com/akrym1582/ExcelRenderer/blob/main/LICENSE)
