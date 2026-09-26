# AI coding agent guide

## Repository

- `src/ExcelRenderer` is the .NET Standard 2.1 library. `src/ExcelRenderer.Tool` is the .NET 10 CLI. Their tests are in the matching projects under `tests`.
- The rendering flow is `ExcelReader` → `ReportDocument` (`Model`) → `ReportLayoutEngine` (`Layout`) → `DrawCommandGeneratorPass` (`Drawing`) → `PdfSharpRenderer` or `PngRenderer`. Markdown export consumes `ReportDocument` through a separate path.
- Keep parsing, layout, drawing commands, and output rendering in their respective layers. Add layout behavior as an `IReportLayoutPass` and place it deliberately in `ReportLayoutEngine`.

## Changes

- Coordinates and dimensions in the model and drawing commands are PDF points. Convert to pixels only at PNG output using the requested DPI.
- Merged ranges use their top-left cell for `RowSpan` and `ColumnSpan`; avoid drawing the remaining cells again.
- When a drawing command or style changes, check both PDFsharp and SkiaSharp renderers. Preserve Excel line style and width separately when converting borders.
- The default Noto Sans JP Regular font is embedded from `third_party/NotoSansJP`. If the embedded resource is unavailable, rendering falls back to system fonts. Keep its SIL Open Font License and `THIRD-PARTY-NOTICES.md` aligned with any font changes.
- Preserve nullable annotations and consider existing callers before changing public APIs. Follow the surrounding C# style and the language used in each file; `README.md` is English and `README.ja.md` is Japanese.
- Document only behavior that is implemented. Keep changes focused and add meaningful tests for changed behavior in the relevant test project.

## Verification

- From the repository root, run `dotnet test ExcelRenderer.slnx` after code changes.
- When package contents or project files change, also verify the affected package with `dotnet pack`.
