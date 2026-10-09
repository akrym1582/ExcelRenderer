# Excel template mapping

`ExcelRenderer.Mapping` is a standalone .NET Standard 2.1 package. It maps CLR
objects or JSON into an XLSX template using ClosedXML 0.105.1. It does not depend
on the renderer, fonts, PDFsharp or SkiaSharp. The CLI references both the mapping
and rendering packages.

## CLI

```sh
excelrenderer xlsx template.xlsx --data data.json -o report.xlsx
excelrenderer pdf template.xlsx --data data.json -o report.pdf
excelrenderer svg template.xlsx --data data.json -o ./svg-output
excelrenderer image template.xlsx --data data.json -o ./png-output
excelrenderer render template.xlsx --data data.json --format png -o ./png-output
```

`xlsx` requires `--data` and only maps and saves the workbook. `--data` is optional
on `pdf`, `image`, `svg`, `markdown`/`md` and `render`; omitting it preserves their
existing behavior. Mapping precedes rendering. A temporary mapped workbook keeps
the original input filename and is deleted on success or failure. Input XLSX and
JSON paths cannot also be the output path. Mapping errors are detected before
opening the output file.

## C# API

```csharp
using ExcelRenderer.Mapping;

ExcelTemplateMapper.Map("template.xlsx", "report.xlsx", new
{
    Name = "Alice",
    IssuedAt = new DateTime(2026, 10, 9),
    Items = new[] { new { Name = "Book", Quantity = 2, Price = 12.50m } },
});

using var template = File.OpenRead("template.xlsx");
using var mapped = new MemoryStream();
ExcelTemplateMapper.MapJson(template, mapped, File.ReadAllText("data.json"));
mapped.Position = 0;
// Pass mapped to ExcelConverter.RenderAsync, or save it as XLSX.
```

Both streams remain open. The mapper writes at the destination's current position;
use an empty stream for a new workbook. For CLR data, public readable properties
and dictionary keys are case-sensitive. Property values retain their CLR types;
objects are not first serialized to JSON. JSON input can also be passed as a
`JsonElement` to `Map`. Keep its owning `JsonDocument` alive during mapping.

`MappingOptions.Culture` defaults to `InvariantCulture`. `MaxOutputRows` defaults
to 100,000 per worksheet and may be set up to Excel's 1,048,576-row limit. Nested
expansion counts toward the limit. Cancellation is checked during planning and
expansion; ClosedXML's synchronous load/save operations cannot be interrupted.
The workbook and expansion plans are held in memory.

## Cell expressions

A text cell starting with `**` is replaced as a whole. Formula cells are left as
formulas. Embedded expressions such as `Name: **name` are ordinary literal text.

| Template text | Meaning |
| --- | --- |
| `**$.name.value` | Resolve from the root object |
| `**name.value` or `**.name.value` | Same root path |
| `**items[0].name` | Resolve a nonnegative array index |
| `**$['a.b']["key|name"]` | Resolve literal property keys |
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

Indentation above illustrates nesting; each line represents an Excel row. Each
array element gets a copy of every row between its matching markers. An empty
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
