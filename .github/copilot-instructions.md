# Copilot Instructions

## Project Overview

This repository contains a .NET library that converts Excel files into an intermediate model, calculates their layout, generates drawing commands, and renders them as PDF documents or page-by-page PNG images. Production code is under `src/ExcelRenderer`, and tests are under `tests/ExcelRenderer.Tests`. The library targets .NET Standard 2.1; the repository uses the .NET 10 SDK for builds and tests.

The main processing flow is:

1. `Excel/ExcelReader.cs` creates the intermediate models in `Model` from a ClosedXML workbook.
2. `Layout/ReportLayoutEngine` runs the layout passes in sequence and creates a `RenderDocument`.
3. `Drawing/DrawCommandGeneratorPass` converts the cells and images to render into drawing commands.
4. `PdfSharp/PdfSharpRenderer` renders the commands to PDF with PDFsharp, while `SkiaSharp/PngRenderer` renders one PNG per page with SkiaSharp.

## Implementation Guidelines

- Keep the intermediate model, layout, drawing, and PDF/PNG output responsibilities separate. Minimize changes that cross these layers.
- Add new layout behavior as an `IReportLayoutPass`, and make its position in `ReportLayoutEngine` explicit. The current order is normalization, print-area resolution, hidden-row and hidden-column handling, column layout, row layout, text measurement, cell-bounds calculation, and pagination.
- Treat coordinates and dimensions as PDF points. Apply page margins during pagination.
- Store `RowSpan` and `ColumnSpan` on the top-left cell of a merged range. Do not render the other cells in that range again.
- Preserve nullable reference type annotations. Before changing a public API, assess the impact on existing callers.
- Do not describe unimplemented features as supported. PNG and JPEG worksheet images can be read and rendered, but images cannot be split across pages.
- Produce one page per PNG file. For multi-page output, use the output-stream factory that receives the page number. Convert points to pixels using the requested DPI.

## Coding Conventions

- Follow the existing C# style, including file-scoped namespaces, record types, and implicit typing where appropriate.
- Do not introduce unrelated refactoring or dependencies.
- Use xUnit for tests. When behavior changes, add or update the corresponding tests in `tests/ExcelRenderer.Tests`.
- Write code, comments, documentation, and user-facing messages in English unless a file is explicitly language-specific, such as `README.ja.md`.

## Verification

After making changes, run the following command from the repository root:

```bash
dotnet test ExcelRenderer.slnx
```
