# Architecture

English | [日本語](architecture.ja.md)

This document describes ExcelRenderer's internal structure, layout passes, and extension points. For installation and usage, see the [README](../README.md).

## Processing flow

```text
Excel file
    │
    ▼
ExcelReader
    │
    ▼
ReportDocument / ReportSheet
    │
    ▼
ReportLayoutEngine
    │
    ├─ NormalizePass
    ├─ ResolvePrintAreaPass
    ├─ HiddenRowColumnPass
    ├─ ColumnLayoutPass
    ├─ RowLayoutPass
    ├─ TextMeasurePass
    ├─ CellBoundsPass
    └─ PaginationPass
    │
    ▼
RenderDocument
    │
    ▼
DrawCommandGeneratorPass
    │
    ├─ FillRectangleCommand
    ├─ DrawBorderCommand
    ├─ DrawTextCommand
    └─ DrawImageCommand
    │
    ▼
PdfSharpRenderer / PngRenderer / SvgRenderer
    │
    ▼
PDF / PNG / SVG
```

Processing has four stages:

1. Read the Excel file
2. Compute the layout
3. Generate drawing commands
4. Render to PDF, PNG, or SVG

Markdown export consumes `ReportDocument` through a separate path.

## 1. Reading the Excel file

`ExcelReader` reads a workbook with ClosedXML and builds the library's own model, `ReportDocument`.

```text
Excel Workbook
    ↓
ExcelReader
    ↓
ReportDocument
    └─ ReportSheet
        ├─ Cells
        ├─ Rows
        ├─ Columns
        ├─ MergedCells
        ├─ Images
        └─ PageSettings
```

`ReportDocument` holds multiple `ReportSheet` instances. At this stage it keeps the information read from Excel; concrete coordinates and page numbers are not yet decided.

Because reading and layout are separate, another input source can be added as long as it produces the same `ReportDocument`.

## 2. Layout

`ReportLayoutEngine` runs layout passes in order.

```csharp
public interface IReportLayoutPass
{
    void Execute(ReportLayoutContext context);
}
```

Each pass reads and updates a shared `ReportLayoutContext`.

```text
ReportSheet → ReportLayoutContext → Pass 1 → Pass 2 → ... → RenderDocument
```

### ReportLayoutContext

| Property | Contents |
| --- | --- |
| `Sheet` | Worksheet being laid out |
| `TextMeasurer` | Implementation that measures rendered text |
| `PrintArea` | Resolved print area |
| `VisibleColumns` | Columns to draw |
| `VisibleRows` | Rows to draw |
| `ColumnLayouts` | Position and width of each column |
| `RowLayouts` | Position and height of each row |
| `TextSizes` | Measured cell text |
| `CellLayouts` | Drawing area of each cell |
| `RenderDocument` | Final page layout |

Each pass uses what earlier passes produced and adds what later passes need.

### Layout pass order

```csharp
new NormalizePass(),
new ResolvePrintAreaPass(),
new HiddenRowColumnPass(),
new ColumnLayoutPass(),
new RowLayoutPass(),
new TextMeasurePass(),
new CellBoundsPass(),
new PaginationPass()
```

Passes depend on each other: cell bounds need column widths and row heights, and pagination needs cell positions and page settings. When adding a pass, check which pass produces its inputs and insert it at a suitable position.

### NormalizePass

Normalizes worksheet information so later passes share common assumptions about cells, rows, and columns.

### ResolvePrintAreaPass

Resolves the print area. A configured print area is used; otherwise the area is derived from the sheet's data. The result is stored in `ReportLayoutContext.PrintArea`.

### HiddenRowColumnPass

Removes hidden rows and columns from the print area and stores the result in `VisibleRows` and `VisibleColumns`. Later passes treat them as having no width or height.

### ColumnLayoutPass / RowLayoutPass

Compute the start position, size, and cumulative position of each visible column and row, and store them in `ColumnLayouts` and `RowLayouts`. Conversion from Excel units to PDF points also happens here.

### TextMeasurePass

Measures the size each cell's text needs using `ITextMeasurer` and stores it in `TextSizes`, considering font family, size, style, wrapping, cell width, and line breaks. `PdfSharpTextMeasurer` is the PDFsharp-based implementation; the interface allows other measurement methods.

With `ITextLayoutService`, the pass also finalizes baselines, run offsets and advances, selected
physical faces, and an explicit effective size. A legacy result with no explicit size remains
distinct from an explicit zero through pagination. PDFsharp uses an internal face key backed by the
selected bytes for both measurement and drawing; Skia likewise accepts memory-backed font data.
Finalized underlines are manual line geometry and are not also enabled on the PDF font.

### CellBoundsPass

Combines column and row layouts into each cell's coordinates and drawing area in `CellLayouts`. A merged cell is treated as one area spanning its rows and columns.

### PaginationPass

Splits pages using paper size, orientation, margins, scaling, and cell positions, and creates the final `RenderDocument`. Both an explicit percentage and fit-to-pages are honored, and cells, text, borders, and images are scaled together. `RenderDocument` holds `RenderPage` instances with page numbers, cells, images, headers, footers, and in-page coordinates.

Pages are split at row and column boundaries only; cells and merged cells are never split.

## 3. Generating drawing commands

`RenderDocument` does not yet depend on PDFsharp. `DrawCommandGeneratorPass` converts it into `DrawCommand` objects. For each page, commands are generated roughly in this order:

1. Backgrounds
2. Borders
3. Cell text
4. Images
5. Headers and footers

Order determines overlap, so backgrounds are generated before text.

- `FillRectangleCommand`: cell background
- `DrawBorderCommand`: cell border
- `DrawTextCommand`: cell text, headers, and footers
- `DrawImageCommand`: worksheet images

Commands separate layout from drawing, so renderers can be added without changing layout and tests can inspect commands directly. Coordinates and dimensions are in PDF points.

## 4. Rendering to PDF / PNG / SVG

`PdfSharpRenderer` processes the commands in order to create a PDF.

| Command | PDFsharp operation |
| --- | --- |
| `FillRectangleCommand` | Fill a rectangle |
| `DrawBorderCommand` | Draw a line |
| `DrawTextCommand` | Draw text |
| `DrawImageCommand` | Draw an image |

Image data is decoded with SkiaSharp before being drawn into the PDF.

`PngRenderer` draws the same commands with SkiaSharp and writes each page as an independent PNG. Dimensions are received in points and converted to pixels at 96 DPI by default (or the requested DPI). Because PNG cannot hold multiple pages, multi-page output uses a stream factory that receives the page number.

`SvgRenderer` shares drawing logic with PNG and writes one self-contained SVG per page in points. Text is converted to vector paths with the font used at generation time, and images are embedded. Viewers do not need the font, but text cannot be searched or copied. Outlining is not an anti-editing feature and does not guarantee complex scripts or color fonts.

When no font manager is supplied, the shared Skia compatibility painter selects a system face once
from the requested family, weight, and slant. Measurement and text/path drawing borrow that same
face, including after wrapping or shrink-to-fit changes the font size.

## Design principles

Conversion is split into interpreting input, computing layout, generating drawing commands, and rendering to an output format, rather than one large routine.

- Layout is split into passes, each with basically one purpose
- Passes exchange information through `ReportLayoutContext`
- `ReportLayoutEngine` does not call PDFsharp; it produces `RenderDocument` and `DrawCommand`
- Drawing is expressed as commands, which makes unit tests, ordering changes, new renderers, and debug output easier

## Extending

### Add a layout pass

Implement `IReportLayoutPass` and register it in `ReportLayoutEngine` at the right position.

```csharp
using ExcelRenderer.Abstractions;
using ExcelRenderer.Layout;

public sealed class CellPaddingPass : IReportLayoutPass
{
    public void Execute(ReportLayoutContext context)
    {
        // read and update context.CellLayouts, etc.
    }
}
```

```csharp
new CellBoundsPass(),
new CellPaddingPass(),
new PaginationPass()
```

The pass list is currently built inside `ReportLayoutEngine`, so adding a pass requires changing the registration order as well as creating the class.

### Add a drawing command

1. Define a new `DrawCommand`
2. Generate it in `DrawCommandGeneratorPass`
3. Draw it in each renderer (`PdfSharpRenderer`, `PngRenderer`, `SvgRenderer`)

For example, a watermark can be a `DrawWatermarkCommand` generated from `Watermark` information.

### Add an output format

A new renderer can take `RenderDocument` or `DrawCommand` as input.

```text
DrawCommand
    ├─ PdfSharpRenderer
    ├─ PngRenderer
    ├─ SvgRenderer
    ├─ CanvasRenderer
    └─ DebugJsonRenderer
```

### Replace text measurement

Text measurement is isolated behind `ITextMeasurer`. Implementations can target PDFsharp, SkiaSharp, a browser-like measurer, or fixed sizes for tests.

## Extension ideas

- Keep SVG data in `ReportImage` and draw it with a `DrawSvgCommand` or an SVG-aware renderer
- Add a `DrawWatermarkCommand` to `DrawCommandGeneratorPass`
- Add section-based page breaks with a `CustomPaginationPass`
- Show cell bounds and page areas with a `DrawDebugBoundsCommand`

## Notes for extensions

- Pass order has dependencies. `CellBoundsPass` uses `ColumnLayouts` and `RowLayouts`, so it cannot run before the layout passes.
- Do not assume too much about properties being set; validate unset state where needed.
- `DrawCommand` order is drawing order. Decide clearly where a new command goes relative to other elements.
- Keep PDFsharp-specific code in `PdfSharpRenderer` and the `ExcelRenderer.PdfSharp` namespace.

## Source layout

```text
src/ExcelRenderer
├─ Abstractions
│  ├─ IReportLayoutPass
│  └─ ITextMeasurer
├─ Drawing
│  ├─ DrawCommand
│  └─ DrawCommandGeneratorPass
├─ Excel
│  └─ ExcelReader
├─ Layout
│  ├─ ReportLayoutEngine
│  ├─ ReportLayoutContext
│  └─ Layout passes
├─ Model
│  ├─ ReportDocument
│  ├─ ReportSheet
│  └─ RenderDocument
├─ PdfSharp
│  ├─ PdfSharpRenderer
│  ├─ PdfSharpTextMeasurer
│  └─ PdfSharpFontResolver
└─ SkiaSharp
   └─ PngRenderer
```

When adding a feature, prefer a new reader, layout pass, drawing command, renderer, or abstraction over adding responsibilities to an existing class.

## Finalized text scaling and PDF font lifetime

`TextLayoutTransform` is the single non-mutating operation for scaling finalized text geometry. It scales sizes, line metrics, run positions, and an explicitly supplied effective font size; an unspecified effective size remains unspecified. Shrink-to-fit first finalizes the source font size, while pagination deliberately preserves unspecified state.

PDF resolved-face registrations snapshot caller-owned bytes and identify the snapshot by content digest and face ID. Registration entries weakly reference immutable bytes; the original resolved font owns its snapshot. Public finalized layouts keep their fonts alive, while expired entries are removed after conversion. Reusing a live face avoids repeated reads and hashing. Main/Core operations share the font gate; see [the two-engine architecture](core-architecture.md).

`FontManager` keeps byte identity separate from resolution identity. A face ID may be shared by aliases backed by identical bytes, but the resolved-result cache is keyed by the selected registration and requested style. This preserves the registered family and style diagnostics while still reusing repeated resolutions of the same registration.

## Finalized and compatibility text paths

PDFsharp and Skia keep text dispatch separate from shape, border, and image dispatch. Each backend's
`TextPainter` owns vertical-text normalization, the page-coordinate clip, rotation, and balanced
save/restore. It then selects exactly one path: finalized layouts consume the stored baseline, run X
position, effective size, and resolved face without wrapping or shrinking again; commands without a
layout use the compatibility path, which retains wrapping, shrink-to-fit, fallback, IVS, and emoji
handling. Backend objects remain in their backend namespace; the shared placement and effective-size
helpers have no PDFsharp or Skia dependency.

## Pagination planning

`PaginationPass` orchestrates pagination rather than owning every calculation. `PageBandBuilder`
creates half-open bands for one axis and is also used unchanged by `PrintScaleResolver` while testing
fit-to-pages scales. `PagePlacement` freezes each page's margins, centering, repeated-title offsets,
body clip, and distinct cell/body-object coordinate maps. `HeaderFooterLayout` expands fields only
after final page numbers and page counts are known. These helpers consume immutable values and do not
mutate `ReportLayoutContext`. `RenderPageBuilder` selects and maps a single page's cells and objects;
it resolves each image or shape anchor once and retains the original bounds while using rotated visual
bounds only for membership. The pass retains print-area preparation, page-order selection, page
construction, and header/footer attachment.

### Explicit selections and final output geometry

`SelectionOptions.Ranges` is validated against selected sheet identities and a checked total cell count before any output sink opens. The request pipeline clones only selection/print settings; it does not remove original cells or change merged spans. `ExplicitRangeGeometryPass` retains full intersecting merge metrics. Visible axes keep original sheet coordinates for explicit selections. `RenderPageBuilder` records body and repeated-title `PageSourceRegion` mappings; continuous explicit selection keeps a fixed source rectangle.

Drawing commands retain finalized text layout. Cell clips are applied individually, and object clips use their original rotated geometry. `DrawCommandBounds` unions visible command boundaries after clipping. `PageViewport` translates that finalized scene and supplies final dimensions to PDF, PNG, SVG and descriptors. Renderer viewport saves are restored in `finally`; PNG limits are checked during preflight. Source cell regions, requested range, original dimensions, crop and padding are optional schema-1 metadata.

`HyperlinkReader` reads XML definitions, relationships, scoped names and literal HYPERLINK arguments without fetching targets or evaluating formulas. Issues stay attached to original definitions until a PDF/Markdown request includes their sources. `HyperlinkPolicy` shares URI safety and simple internal-target resolution. PDF resolution uses selected final pages and their source regions; `PdfHyperlinkWriter` adds annotations only after import into the final document, converting top-origin point coordinates to PDF default coordinates. Markdown keeps plain text and targets separate, uses one formatter for every display path, and emits stable required anchors and explicit lists for blank or range sources. None and image output do not evaluate hyperlink diagnostics.
