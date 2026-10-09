# Excel template mapping

English | [日本語](https://github.com/akrym1582/ExcelRenderer/blob/main/docs/mapping.ja.md)

`ExcelRenderer.Mapping` is a standalone .NET Standard 2.1 package. It maps CLR
objects or JSON into an XLSX template using ClosedXML 0.105.1. It does not depend
on the renderer, fonts, PDFsharp or SkiaSharp. The CLI references both the mapping
and rendering packages.

Use it to keep the layout in Excel and supply the values from your application.
The result is an ordinary XLSX workbook, which you can open in Excel or pass to
ExcelRenderer for PDF, PNG, SVG or Markdown output.

## Install

For C# applications that only generate XLSX files, run this in the application
project directory:

```sh
dotnet add package ExcelRenderer.Mapping
```

Target a framework compatible with .NET Standard 2.1, such as .NET 8 or .NET 10.
.NET Framework is not supported. The mapping library does not require fonts,
ExcelRenderer or an installation of Microsoft Excel.

For command-line use, install the .NET 10 SDK and the tool:

```sh
dotnet tool install --global ExcelRenderer.Tool
excelrenderer xlsx --help
```

If the tool is already installed, use `dotnet tool update --global ExcelRenderer.Tool`.
The CLI includes mapping, rendering and bundled fonts; you do not need to install
`ExcelRenderer.Mapping` separately for CLI use.

These commands require NuGet.org as a package source and a published version
containing mapping. Before publication, build a local package using the
[source installation instructions](https://github.com/akrym1582/ExcelRenderer/blob/main/README.md#development).

## First workbook: an invoice

### 1. Create the template

Create a blank workbook in Excel or another XLSX editor, name its worksheet `Invoice`, and enter the
following values. Save it as `template.xlsx` in a working directory.

| Row | Column A | Column B | Column C | Column D |
| --- | --- | --- | --- | --- |
| 1 | Invoice | | | |
| 2 | Customer | `**Customer` | | |
| 3 | Issued | `**IssuedAt \| date("yyyy/MM/dd")` | | |
| 4 | Item | Quantity | Unit price | Line total |
| 5 | `**Items[*].Name` | `**Items[*].Quantity` | `**Items[*].Price` | `=B5*C5` |
| 6 | Total | | | `**Total` |

Enter the `**` expressions as cell text, without an initial `=`. D5 is an actual
Excel formula. The backslash before the pipe in the Markdown table is only table
escaping: B3 should contain `**IssuedAt | date("yyyy/MM/dd")`.
Use ordinary cells, rather than an Excel Table, for row 5. You can set fonts,
column widths, borders and number formats in Excel. Set C5, D5 and D6 to
`#,##0.00` and the print area to A1:D6 for this example.
If you do not have an XLSX editor, use the
[C# template generator](#create-the-example-template-without-excel) below.

### 2. Create the data

Save this as `data.json` alongside `template.xlsx`:

```json
{
  "Customer": "Alice",
  "IssuedAt": "2026-10-09",
  "Items": [
    { "Name": "Book", "Quantity": 2, "Price": 12.50 },
    { "Name": "Pen", "Quantity": 3, "Price": 2.00 }
  ],
  "Total": 31.00
}
```

The names match the template exactly, including uppercase letters. `Total` is
supplied by the data so this example does not depend on a formula range extending
across inserted rows.

### 3. Generate and inspect the XLSX

Run this from the directory containing both files:

```sh
excelrenderer xlsx template.xlsx --data data.json -o report.xlsx
```

Open `report.xlsx`. B2 contains `Alice`, B3 contains `2026/10/09`, and row 5
has expanded into two item rows:

| Row | Item | Quantity | Unit price | Line total |
| --- | --- | --- | --- | --- |
| 5 | Book | 2 | 12.50 | 25.00 |
| 6 | Pen | 3 | 2.00 | 6.00 |
| 7 | Total | | | 31.00 |

The formula in D6 becomes `=B6*C6`. Row styles and the footer move with the
expansion. `template.xlsx` and `data.json` remain the inputs for the next run.
A successful `xlsx` run replaces an existing output XLSX; use a separate output
path if you want to keep an earlier report. Mapping errors are detected before
opening the destination.

### 4. Render the report if needed

Render the XLSX you just inspected:

```sh
excelrenderer pdf report.xlsx -o report.pdf
```

Or map and render directly from the template in one command:

```sh
excelrenderer pdf template.xlsx --data data.json -o report.pdf
excelrenderer svg template.xlsx --data data.json -o ./svg-output
excelrenderer image template.xlsx --data data.json -o ./png-output
excelrenderer markdown template.xlsx --data data.json -o report.md
excelrenderer render template.xlsx --data data.json --format png -o ./png-output
```

Choose one command at a time and a fresh rendering output path. Rendering commands
reject existing output files or non-empty output directories.

## CLI reference

| Command | Output path | Data option |
| --- | --- | --- |
| `xlsx` | XLSX file | `--data <data.json>` required |
| `pdf` | PDF file | `--data <data.json>` optional |
| `image` / `svg` | Output directory | `--data <data.json>` optional |
| `markdown` / `md` | Markdown file | `--data <data.json>` optional |
| `render` | PDF file, or PNG/SVG/Markdown directory | `--data <data.json>` optional; `--format` required |

`xlsx` requires `--data` and only maps and saves the workbook. `--data` is optional
on `pdf`, `image`, `svg`, `markdown`/`md` and `render`; omitting it preserves their
existing behavior. Mapping precedes rendering. A temporary mapped workbook keeps
the original input filename and is deleted on success or failure. Input XLSX and
JSON paths cannot also be the output path. Mapping errors are detected before
opening the output file. Mapping processes every worksheet, even if rendering
selects a single sheet with `--sheet`. Quote paths that contain spaces.

## C# API

The examples below use the invoice template above. Run them in the directory
containing `template.xlsx` and `data.json`. For a new application, create a
console project with `dotnet new console -n MappingDemo`, enter its directory,
add `ExcelRenderer.Mapping`, and replace `Program.cs` with one of these examples.
Copy the input files into that directory and run `dotnet run`.

### Map a C# object

```csharp
using System;
using ExcelRenderer.Mapping;

ExcelTemplateMapper.Map("template.xlsx", "report.xlsx", new
{
    Customer = "Alice",
    IssuedAt = new DateTime(2026, 10, 9),
    Items = new[]
    {
        new { Name = "Book", Quantity = 2, Price = 12.50m },
        new { Name = "Pen", Quantity = 3, Price = 2.00m },
    },
    Total = 31.00m,
});
```

CLR data uses public readable properties and dictionary keys. Names are
case-sensitive. Values retain their CLR types; the object is not first serialized
to JSON. `Items` may also come from your application's enumerable collection.

### Map a JSON file

Use `Map` with a `JsonElement` for file-to-file mapping:

```csharp
using System.IO;
using System.Text.Json;
using ExcelRenderer.Mapping;

using var document = JsonDocument.Parse(File.ReadAllText("data.json"));
ExcelTemplateMapper.Map("template.xlsx", "report.xlsx", document.RootElement);
```

Keep the owning `JsonDocument` alive until mapping finishes.
`MapJson` takes JSON **text**, rather than a JSON file path, and works with streams:

```csharp
using System.IO;
using ExcelRenderer.Mapping;

using var template = File.OpenRead("template.xlsx");
using var mapped = new MemoryStream();
ExcelTemplateMapper.MapJson(template, mapped, File.ReadAllText("data.json"));
mapped.Position = 0;
using var output = File.Create("report.xlsx");
mapped.CopyTo(output);
```

Both streams remain open. The mapper writes at the destination's current position;
use an empty stream for a new workbook. Rewind a mapped stream before reading it
or passing it to `ExcelConverter.RenderAsync`.

### Render from C#

Add `ExcelRenderer` to the application and optionally add `ExcelRenderer.Fonts`
for bundled Japanese/emoji fonts. Mapping-only applications do not need either
package. After generating `report.xlsx`, render it with:

```csharp
using ExcelRenderer;

await ExcelConverter.ConvertToPdfAsync("report.xlsx", "report.pdf");
```

Rendering needs suitable fonts; see the
[font installation guide](https://github.com/akrym1582/ExcelRenderer/blob/main/README.md#fonts).

### Options and errors

`MappingOptions.Culture` defaults to `InvariantCulture`. `MaxOutputRows` defaults
to 100,000 per worksheet and may be set up to Excel's 1,048,576-row limit. Nested
expansion and ordinary template rows count toward the limit. Cancellation is checked during planning and
expansion; ClosedXML's synchronous load/save operations cannot be interrupted.
The workbook and expansion plans are held in memory. CLI mapping uses the default
options; configure options through the C# API when needed.

```csharp
using System.Globalization;
using System.IO;
using System.Text.Json;
using ExcelRenderer.Mapping;

using var document = JsonDocument.Parse(File.ReadAllText("data.json"));
var options = new MappingOptions
{
    Culture = CultureInfo.GetCultureInfo("ja-JP"),
    MaxOutputRows = 200_000,
};
try
{
    ExcelTemplateMapper.Map("template.xlsx", "report.xlsx", document.RootElement, options);
}
catch (MappingException error)
{
    Console.Error.WriteLine($"{error.SheetName}!{error.CellAddress}: {error.Message}");
}
```

Errors identify the **original** template cell, even after nested expansion.
Invalid JSON and file access failures are separate exceptions, rather than
`MappingException`. The file API validates and generates the workbook before
opening the destination, so mapping errors leave an existing output file intact.

## Cell expressions

A text cell starting with `**` is replaced as a whole. Formula cells are left as
formulas. Embedded expressions such as `Name: **name` are ordinary literal text.

| Template text | Meaning |
| --- | --- |
| `**$.name.value` | Resolve from the root object |
| `**name.value` or `**.name.value` | Same root path |
| `**items[0].name` | Resolve a nonnegative array index |
| `**$['a.b']["key\|name"]` | Resolve literal property keys |
| `\**literal` | Emit the literal `**literal` |
| `**$` | Map a scalar root value |

Supported JSONPath syntax comprises property access, quoted bracket keys,
nonnegative array indices and `[*]`. Bracket keys support escaped matching quotes
and backslashes. Filters, recursive descent, slices, unions and multiple wildcards
in one path are rejected. Use nested blocks for nested arrays.

A missing property/index is an error; an explicit JSON/CLR null produces an empty
cell. Objects and arrays cannot be assigned to a scalar cell. Unsupported CLR
scalar types require explicit `format()` when they implement `IFormattable`.
Numeric values, booleans, `DateTime` and `TimeSpan` retain Excel types. Excel uses
floating-point numbers, so large integers and high-precision decimals are subject
to Excel's precision limits. Use formatting to preserve their textual representation.

## Formatting

```text
**price | format("N2")
**price | format("C0", "ja-JP")
**items[*].code | format("0000")
**issuedAt | date("yyyy/MM/dd")
```

`format(format[, culture])` calls `IFormattable.ToString` with the value's CLR type.
The optional culture overrides `MappingOptions.Culture`. Arguments are JSON string
literals; pipe characters within quoted path keys do not split the expression.
Formatting produces an Excel **text** cell. Without formatting, the template's
Excel number format controls presentation and numbers remain usable in formulas.

`date(format[, culture])` formats a CLR `DateTime`/`DateTimeOffset`, or converts an
ISO 8601 JSON string only when explicitly requested. Accepted strings are
`yyyy-MM-dd` or `yyyy-MM-ddTHH:mm:ss`, with optional 1–7 fractional digits and an
optional `Z` or `±HH:mm` offset. Strings without `date()` remain strings. No local
machine culture is used to guess dates. Invalid values or unsupported formats are
errors; null remains an empty cell.

## Single-row arrays

For example, put these cells on the same row:

```text
A5: **items[*].name
B5: **$.items[*].quantity
C5: Fixed text
D5: **title
```

The whole row is emitted once per element, including fixed cells and root paths.
All wildcard cells on one row must reference the same normalized array prefix.
An empty array removes that row. Separate rows are independent repetitions.

## Multiple-row and nested arrays

Start/end markers occupy dedicated rows with exactly one content cell and no
merged ranges. Both marker rows disappear from the output.

```text
**@start-array $.orders[*] as order
  **@order.number
  **@start-array @order.lines[*] as line
    **@line.name
    **@line.quantity
    **@order.number
  **@end-array
  **@order.total
**@end-array
```

Indentation above illustrates nesting; each line represents an Excel row. Do not
enter leading spaces before the actual markers or expressions. Each array
element gets a copy of every row between its matching markers. An empty
array removes the complete block. Alias names are required and cannot shadow an
enclosing alias; independent sibling blocks can reuse names. Aliases are only
visible inside their block and nested children. `@order` refers to the bound CLR
object/JSON element, while `$` and omitted root prefixes always refer to the root.
Alias paths are a mapper extension to JSONPath.

Inside a block, use explicit nested markers rather than implicit wildcard-row
repetition. Start-array paths must end in `[*]`. Marker mismatch, unknown aliases,
and unsupported path syntax are validated even when an array is empty. Missing
data in an uninstantiated empty block is not evaluated.

## Page breaks

`**@page-break` clears its cell and inserts a horizontal break immediately above
its row and a vertical break immediately left of its column. At C10 this means a
break after row 9 and after column B. No break is added before row 1 or column A.
The cell's row remains in the output. Repeated directives apply at every expanded
position; duplicate breaks are collapsed.

Existing manual row breaks follow copied rows; column breaks are retained.
A sheet with a page-break directive switches from fit-to-page to explicit scale
mode (retaining a positive scale or using 100%). This makes manual breaks effective
in Excel and in SVG/PNG/PDF output. Other sheets retain their original print mode.

## Excel behavior and limitations

Row expansion uses ClosedXML row insertion/deletion and range copying. It preserves
cell styles, row height, hidden state, outline levels, relative formulas, ordinary
merged ranges, copied conditional formatting and data validation. Existing formula
references, defined names and print areas follow ClosedXML's Excel row-operation
semantics. Ranges ending exactly at a repeated region's boundary do not necessarily
extend to all copies: design the template's totals accordingly or supply totals in
the input data. References to deleted marker/empty-array rows can become `#REF!`.

Merges entirely inside a repeated block are supported. Merges crossing an array
boundary, merged marker rows, Excel tables intersecting repeated regions, and
pictures intersecting repeated regions are rejected with template diagnostics.
Other unsupported workbook features depend on ClosedXML's load/save support;
use ordinary cell-based templates for the repeated regions.

Supported formulas are evaluated before saving; formulas are retained and automatic
recalculation is requested for Excel. Unsupported functions may produce Excel error
values in the cached output and therefore cannot be relied upon for immediate
rendering. Compute those values in C#/JSON instead.

`MappingException` exposes `SheetName`, `CellAddress` and `Expression` referring to
the original template, including errors encountered after nested expansion.

## Create the example template without Excel

You can also generate the same invoice template from C#. In the console project
with `ExcelRenderer.Mapping` installed, replace `Program.cs` with this code and
run `dotnet run` once. ClosedXML is supplied by the mapping package's dependencies.
Then replace `Program.cs` with a mapping example above, or use the CLI.

```csharp
using ClosedXML.Excel;

using var workbook = new XLWorkbook();
var sheet = workbook.AddWorksheet("Invoice");
sheet.Cell("A1").Value = "Invoice";
sheet.Cell("A2").Value = "Customer";
sheet.Cell("B2").Value = "**Customer";
sheet.Cell("A3").Value = "Issued";
sheet.Cell("B3").Value = "**IssuedAt | date(\"yyyy/MM/dd\")";
sheet.Cell("A4").Value = "Item";
sheet.Cell("B4").Value = "Quantity";
sheet.Cell("C4").Value = "Unit price";
sheet.Cell("D4").Value = "Line total";
sheet.Cell("A5").Value = "**Items[*].Name";
sheet.Cell("B5").Value = "**Items[*].Quantity";
sheet.Cell("C5").Value = "**Items[*].Price";
sheet.Cell("D5").FormulaA1 = "B5*C5";
sheet.Cell("A6").Value = "Total";
sheet.Cell("D6").Value = "**Total";
sheet.Columns(1, 4).Width = 22;
sheet.Range("A4:D4").Style.Font.Bold = true;
sheet.Range("C5:D6").Style.NumberFormat.Format = "#,##0.00";
sheet.PageSetup.PrintAreas.Add("A1:D6");
workbook.SaveAs("template.xlsx");
```

## Troubleshooting

| Symptom | What to check |
| --- | --- |
| NuGet cannot find `ExcelRenderer.Mapping` | Enable NuGet.org and check that a mapping release is published. Until then, use the local package instructions linked under Install. |
| `xlsx` or `--data` is not recognized | Update `ExcelRenderer.Tool` to a published version containing mapping. `excelrenderer xlsx --help` should show the command. |
| An expression remains as text | It must start at the beginning of a text cell. `Customer: **Customer` is literal text; put the label and expression in separate cells. |
| `Property '…' was not found` | Match property names and capitalization exactly. Missing properties and out-of-range indices are errors; explicit `null` gives a blank cell. |
| A cell requires a scalar value | Select a property such as `**Customer.Name`, an index, or use an array repetition. An entire object or array cannot occupy a scalar cell. |
| `format()` fails or numbers stop working in formulas | Formatting requires an `IFormattable` value and creates text. For numeric formulas, keep the value unformatted and set the Excel cell's number format. For JSON numbers, use formats such as `0000` or `N2` rather than integer-only CLR formats such as `D4`. |
| A date stays as an ISO string | Use `date("yyyy/MM/dd")` explicitly. Strings are not automatically converted to dates. |
| A repeated row disappears | An empty array deletes its row or block. Pass at least one element if you want a data row. |
| Markers report a dedicated-row or alias error | Put each start/end marker on its own unmerged row. Use a matching pair and refer to aliases only inside their block. |
| Expansion exceeds `MaxOutputRows` | Reduce the data size or set a suitable `MappingOptions.MaxOutputRows` through C#. Ordinary rows also count. The CLI uses the 100,000-row default. |
| A total only includes the first item, or a formula becomes `#REF!` | Formula ranges follow ClosedXML row operations. Supply totals in the input, and avoid referencing marker rows or rows deleted by empty arrays. |
| A formula shows `#NAME?` in rendered output | ClosedXML cannot evaluate every Excel function. Compute the value in the input; Excel recalculation after opening is not available during immediate rendering. |
| Rendering reports that output already exists | Use a fresh output file or an empty output directory. Successful `xlsx` mapping, unlike rendering, replaces an existing output XLSX. |
| An unselected worksheet causes a mapping error | Mapping evaluates all worksheets before rendering selection. Fix its expressions or remove that worksheet from the template. |
